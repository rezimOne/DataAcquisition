using DataAcquisition.Worker.MTConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static DataAcquisition.Worker.Tests.MTConnect.Docs;

namespace DataAcquisition.Worker.Tests.MTConnect;

public sealed class MTConnectSynchronizerTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    private readonly FakeAgent _agent = new();
    private readonly RecordingSink _sink = new();
    private readonly FakeLogger<MTConnectSynchronizer> _logger = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MTConnectSynchronizer _synchronizer;

    public MTConnectSynchronizerTests() =>
        _synchronizer = new MTConnectSynchronizer(_agent, _sink, Options.Create(new MTConnectOptions()), _time, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<TimeSpan> Step() => _synchronizer.StepAsync(Ct);

    private IReadOnlyList<FakeLogRecord> Logs(LogLevel level) =>
        _logger.Collector.GetSnapshot().Where(r => r.Level == level).ToList();

    private async Task SyncAsync(long instanceId = InstanceA, long next = 4)
    {
        _agent.Probe(Devices(instanceId)).Current(Streams(instanceId, next, next - 1, next - 1));
        await Step();
        _agent.Calls.Clear();
        _sink.Batches.Clear();
        _logger.Collector.Clear();
    }

    // AC-03

    [Fact]
    public async Task StepAsync_FirstStep_ProbesThenDeliversCurrentSnapshot()
    {
        _agent.Probe(Devices()).Current(Streams(InstanceA, 4, 3, 3, 1, 2));

        var delay = await Step();

        Assert.Equal(["probe", "current"], _agent.Calls);
        var batch = Assert.Single(_sink.Batches);
        Assert.Equal((Device, InstanceA, ObservationSource.Current), (batch.DeviceName, batch.InstanceId, batch.Source));
        Assert.Equal([1L, 2L, 3L], _sink.Sequences);
        Assert.Equal(Poll, delay);
    }

    [Fact]
    public async Task StepAsync_ContinuousSampling_UsesNextSequenceAndDeliversEachObservationOnceInOrder()
    {
        _agent.Probe(Devices()).Current(Streams(InstanceA, 4, 3, 1, 2, 3))
            .Sample(Streams(InstanceA, 6, 5, 5, 4))
            .Sample(Streams(InstanceA, 6, 5))
            .Sample(Streams(InstanceA, 7, 6, 6));

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            delays.Add(await Step());
        }

        Assert.Equal(
            ["probe", "current", "sample?from=4&count=1000", "sample?from=6&count=1000", "sample?from=6&count=1000"],
            _agent.Calls);
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], _sink.Sequences);
        Assert.Equal(3, _sink.Batches.Count);
        Assert.All(_sink.Batches.Skip(1), b => Assert.Equal(ObservationSource.Sample, b.Source));
        Assert.All(delays, d => Assert.Equal(Poll, d));
    }

    [Fact]
    public async Task StepAsync_AgentHasMoreData_ReturnsZeroDelayToCatchUp()
    {
        await SyncAsync();
        _agent.Sample(Streams(InstanceA, 6, 500, 4, 5));

        var delay = await Step();

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public async Task StepAsync_ObservationsOfTwoDevices_DeliversOneBatchPerDevice()
    {
        await SyncAsync();
        _agent.Sample(StreamsOf(InstanceA, 7, 6, Observation(4), Observation(5, device: "LATHE2"), Observation(6)));

        await Step();

        Assert.Equal([(Device, 2), ("LATHE2", 1)], _sink.Batches.Select(b => (b.DeviceName, b.Observations.Count)));
    }

    [Fact]
    public async Task StepAsync_KnownDataItem_TakesTypeSubTypeAndUnitsFromProbe()
    {
        await SyncAsync();
        _agent.Sample(StreamsOf(InstanceA, 5, 4, Observation(4) with { Type = "FROM_ELEMENT", SubType = null }));

        await Step();

        var observation = Assert.Single(_sink.Batches.SelectMany(b => b.Observations));
        Assert.Equal(("POSITION", "ACTUAL", "MILLIMETER"), (observation.Type, observation.SubType, observation.Units));
    }

    [Fact]
    public async Task StepAsync_UnknownDataItem_KeepsElementTypeAndWarnsOnce()
    {
        await SyncAsync();
        _agent.Sample(StreamsOf(InstanceA, 5, 4, Observation(4, dataItemId: "newitem")))
            .Sample(StreamsOf(InstanceA, 6, 5, Observation(5, dataItemId: "newitem")));

        await Step();
        await Step();

        Assert.Equal(2, _sink.Batches.Count);
        Assert.Equal("POSITION", _sink.Batches[0].Observations[0].Type);
        var warning = Assert.Single(Logs(LogLevel.Warning));
        Assert.Contains("newitem", warning.Message);
    }

    // AC-04

    [Fact]
    public async Task StepAsync_InstanceIdChanged_LogsAndResynchronizesWithProbe()
    {
        await SyncAsync();
        _agent.Sample(Streams(InstanceB, 50, 49, 48, 49))
            .Probe(Devices(InstanceB))
            .Current(Streams(InstanceB, 60, 59, 10, 59))
            .Sample(Streams(InstanceB, 3, 59, 1, 2));

        await Step();
        var catchUpDelay = await Step();

        // New instance's buffer is not full: resume from its firstSequence, nothing collected since the restart is lost.
        Assert.Equal(["sample?from=4&count=1000", "probe", "current", "sample?from=1&count=1000"], _agent.Calls);
        Assert.Equal([10L, 59L, 1L, 2L], _sink.Sequences);
        Assert.Equal(TimeSpan.Zero, catchUpDelay);
        Assert.All(_sink.Batches, b => Assert.Equal(InstanceB, b.InstanceId));
        Assert.Contains(Logs(LogLevel.Information), r => r.Message.Contains("restarted") && r.Message.Contains(InstanceB.ToString()));
    }

    [Fact]
    public async Task StepAsync_RestartedAgentAnswersOutOfRange_LogsRestartAndReprobes()
    {
        // Found in the manual test (AC-08): a restarted agent has lower sequences, so /sample?from=<old next>
        // returns OUT_OF_RANGE instead of a document with the new instanceId.
        await SyncAsync(next: 6900);
        _agent.SampleThrows(new MTConnectAgentErrorException(MTConnectAgentErrorException.OutOfRange, "'from' outside buffer"))
            .Current(Streams(InstanceB, 28, 27, 27))
            .Probe(Devices(InstanceB))
            .Sample(Streams(InstanceB, 29, 28, 28));

        await Step();
        await Step();

        Assert.Equal(["sample?from=6900&count=1000", "current", "probe", "sample?from=1&count=1000"], _agent.Calls);
        Assert.Equal([27L, 28L], _sink.Sequences);
        Assert.Contains(Logs(LogLevel.Information), r => r.Message.Contains("restarted") && r.Message.Contains(InstanceB.ToString()));
        Assert.Empty(Logs(LogLevel.Warning));
    }

    // AC-05

    [Fact]
    public async Task StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange()
    {
        await SyncAsync();
        // Full buffer: firstSequence moves with every new observation, so resume SampleCount (1000) ahead of it.
        var fullBuffer = new StreamsHeader(InstanceA, NextSequence: 231_072, FirstSequence: 100_000, LastSequence: 231_071, BufferSize: 131_072);
        _agent.SampleThrows(new MTConnectAgentErrorException(MTConnectAgentErrorException.OutOfRange, "'from' outside buffer"))
            .Current(new StreamsDocument(fullBuffer, [Observation(231_000)]))
            .Sample(Streams(InstanceA, 101_002, 231_071, 101_000, 101_001));

        await Step();
        var catchUpDelay = await Step();

        Assert.Equal(["sample?from=4&count=1000", "current", "sample?from=101000&count=1000"], _agent.Calls);
        Assert.Equal([231_000L, 101_000L, 101_001L], _sink.Sequences);
        Assert.Equal(TimeSpan.Zero, catchUpDelay);
        var warning = Assert.Single(Logs(LogLevel.Warning));
        Assert.Equal("4", warning.GetStructuredStateValue("LostFrom"));
        Assert.Equal("100999", warning.GetStructuredStateValue("LostTo"));
        Assert.Equal(InstanceA.ToString(), warning.GetStructuredStateValue("InstanceId"));
    }

    [Fact]
    public async Task StepAsync_OutOfRangeWithNotFullBuffer_ResumesFromFirstSequence()
    {
        await SyncAsync();
        var partialBuffer = new StreamsHeader(InstanceA, NextSequence: 5_000, FirstSequence: 100, LastSequence: 4_999, BufferSize: 131_072);
        _agent.SampleThrows(new MTConnectAgentErrorException(MTConnectAgentErrorException.OutOfRange, "'from' outside buffer"))
            .Current(new StreamsDocument(partialBuffer, [Observation(4_999)]))
            .Sample(Streams(InstanceA, 102, 4_999, 100, 101));

        await Step();
        await Step();

        Assert.Equal(["sample?from=4&count=1000", "current", "sample?from=100&count=1000"], _agent.Calls);
        var warning = Assert.Single(Logs(LogLevel.Warning));
        Assert.Equal(("4", "99"), (warning.GetStructuredStateValue("LostFrom"), warning.GetStructuredStateValue("LostTo")));
    }

    // AC-06

    public static TheoryData<Exception> TransientErrors => new()
    {
        new HttpRequestException("No connection could be made."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()),
        new HttpRequestException("MTConnect agent returned HTTP 500.", null, System.Net.HttpStatusCode.InternalServerError),
        new MTConnectFormatException("Response is not well-formed XML."),
        new MTConnectAgentErrorException("UNSUPPORTED", "unknown path"),
    };

    [Theory]
    [MemberData(nameof(TransientErrors))]
    public async Task StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(Exception error)
    {
        await SyncAsync();
        _agent.SampleThrows(error).Sample(Streams(InstanceA, 5, 4, 4));

        var errorDelay = await Step();
        var okDelay = await Step();

        Assert.Equal(TimeSpan.FromSeconds(1), errorDelay);
        Assert.Equal(Poll, okDelay);
        Assert.Equal(["sample?from=4&count=1000", "sample?from=4&count=1000"], _agent.Calls);
        Assert.Equal([4L], _sink.Sequences);
        Assert.Single(Logs(LogLevel.Warning));
    }

    [Fact]
    public async Task StepAsync_RepeatedErrors_DoublesDelayUpToMaxAndResetsAfterSuccess()
    {
        await SyncAsync();
        for (var i = 0; i < 7; i++)
        {
            _agent.SampleThrows(new HttpRequestException("down"));
        }
        _agent.Sample(Streams(InstanceA, 4, 3)).SampleThrows(new HttpRequestException("down again"));

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 9; i++)
        {
            delays.Add(await Step());
        }

        Assert.Equal([1, 2, 4, 8, 16, 30, 30, 1, 1], delays.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task StepAsync_SinkFails_RedeliversSameBatch()
    {
        await SyncAsync();
        _sink.FailuresToThrow = 1;
        _agent.Sample(Streams(InstanceA, 6, 5, 4, 5)).Sample(Streams(InstanceA, 6, 5, 4, 5));

        var failedDelay = await Step();
        await Step();

        Assert.Equal(TimeSpan.FromSeconds(1), failedDelay);
        Assert.Equal(["sample?from=4&count=1000", "sample?from=4&count=1000"], _agent.Calls);
        Assert.Equal([4L, 5L], _sink.Sequences);
        Assert.Single(Logs(LogLevel.Warning));
    }

    [Fact]
    public async Task StepAsync_CurrentFailsAfterProbe_RetriesCurrentWithoutProbe()
    {
        _agent.Probe(Devices());
        _agent.Currents.Enqueue(_ => Task.FromException<StreamsDocument>(new HttpRequestException("down")));
        _agent.Current(Streams(InstanceA, 4, 3, 3));

        await Step();
        await Step();

        Assert.Equal(["probe", "current", "current"], _agent.Calls);
        Assert.Equal([3L], _sink.Sequences);
    }

    // AC-07

    [Fact]
    public async Task RunAsync_StoppedWhileWaiting_CompletesWithoutWarnings()
    {
        _agent.Probe(Devices()).Current(Streams(InstanceA, 4, 3, 3));
        var timeProvider = new TimerCountingTimeProvider();
        var synchronizer = new MTConnectSynchronizer(_agent, _sink, Options.Create(new MTConnectOptions()), timeProvider, _logger);
        using var stopping = new CancellationTokenSource();

        var run = synchronizer.RunAsync(stopping.Token);
        await WaitUntilAsync(() => timeProvider.CreatedTimers == 1);
        await stopping.CancelAsync();
        await run;

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Empty(Logs(LogLevel.Warning));
        Assert.Empty(Logs(LogLevel.Error));
    }

    [Fact]
    public async Task RunAsync_StoppedDuringRequest_CompletesWithoutWarnings()
    {
        var requestStarted = new TaskCompletionSource();
        _agent.Probes.Enqueue(async ct =>
        {
            requestStarted.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        using var stopping = new CancellationTokenSource();

        var run = _synchronizer.RunAsync(stopping.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await stopping.CancelAsync();
        await run;

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Empty(Logs(LogLevel.Warning));
        Assert.Empty(Logs(LogLevel.Error));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TimerCountingTimeProvider : FakeTimeProvider
    {
        private int _createdTimers;

        public int CreatedTimers => Volatile.Read(ref _createdTimers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _createdTimers);
            return timer;
        }
    }
}
