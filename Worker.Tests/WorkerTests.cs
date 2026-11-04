using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DataAcquisition.Worker.Tests;

public sealed class WorkerTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeLogger<Worker> _logger = new();
    private readonly TimerCountingTimeProvider _timeProvider = new();

    [Fact]
    public async Task ExecuteAsync_AfterStartAndOneSecond_LogsTwice()
    {
        using var worker = new Worker(_logger, _timeProvider);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _timeProvider.CreatedTimers == 1);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => _timeProvider.CreatedTimers == 2);

        var records = _logger.Collector.GetSnapshot();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.StartsWith("Worker running at", record.Message);
        });

        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_BeforeOneSecondElapsed_LogsOnce()
    {
        using var worker = new Worker(_logger, _timeProvider);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _timeProvider.CreatedTimers == 1);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(999));

        Assert.Equal(1, _logger.Collector.Count);

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => _logger.Collector.Count == 2);

        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_CompletesWithoutExceptionAndStopsLogging()
    {
        using var worker = new Worker(_logger, _timeProvider);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _timeProvider.CreatedTimers == 1);

        var exception = await Record.ExceptionAsync(
            () => worker.StopAsync(TestContext.Current.CancellationToken));
        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(exception);
        Assert.NotNull(worker.ExecuteTask);
        Assert.True(worker.ExecuteTask.IsCompleted);
        Assert.Equal(1, _logger.Collector.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    // Worker starts ExecuteAsync in the background, so tests must wait until it
    // has scheduled its delay before advancing fake time.
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
