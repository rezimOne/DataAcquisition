using DataAcquisition.Worker.MTConnect;

namespace DataAcquisition.Worker.Tests.MTConnect;

/// <summary>Scripted agent: each call takes the next response (or exception) from its queue and is recorded.</summary>
internal sealed class FakeAgent : IMTConnectAgent
{
    public List<string> Calls { get; } = [];

    public Queue<Func<CancellationToken, Task<DevicesDocument>>> Probes { get; } = new();

    public Queue<Func<CancellationToken, Task<StreamsDocument>>> Currents { get; } = new();

    public Queue<Func<CancellationToken, Task<StreamsDocument>>> Samples { get; } = new();

    public Task<DevicesDocument> ProbeAsync(CancellationToken cancellationToken)
    {
        Calls.Add("probe");
        return Next(Probes, cancellationToken);
    }

    public Task<StreamsDocument> CurrentAsync(CancellationToken cancellationToken)
    {
        Calls.Add("current");
        return Next(Currents, cancellationToken);
    }

    public Task<StreamsDocument> SampleAsync(long from, int count, CancellationToken cancellationToken)
    {
        Calls.Add($"sample?from={from}&count={count}");
        return Next(Samples, cancellationToken);
    }

    public FakeAgent Probe(DevicesDocument document) => Enqueue(Probes, document);

    public FakeAgent Current(StreamsDocument document) => Enqueue(Currents, document);

    public FakeAgent Sample(StreamsDocument document) => Enqueue(Samples, document);

    public FakeAgent SampleThrows(Exception exception)
    {
        Samples.Enqueue(_ => Task.FromException<StreamsDocument>(exception));
        return this;
    }

    private FakeAgent Enqueue<T>(Queue<Func<CancellationToken, Task<T>>> queue, T value)
    {
        queue.Enqueue(_ => Task.FromResult(value));
        return this;
    }

    private static Task<T> Next<T>(Queue<Func<CancellationToken, Task<T>>> queue, CancellationToken cancellationToken) =>
        queue.TryDequeue(out var next)
            ? next(cancellationToken)
            : Task.FromException<T>(new InvalidOperationException("Unexpected call to the fake agent."));
}

internal sealed class RecordingSink : IObservationSink
{
    public List<ObservationBatch> Batches { get; } = [];

    public int FailuresToThrow { get; set; }

    public Task ReceiveAsync(ObservationBatch batch, CancellationToken cancellationToken)
    {
        if (FailuresToThrow > 0)
        {
            FailuresToThrow--;
            throw new InvalidOperationException("Sink failure.");
        }
        Batches.Add(batch);
        return Task.CompletedTask;
    }

    public IEnumerable<long> Sequences => Batches.SelectMany(b => b.Observations).Select(o => o.Sequence);
}

internal static class Docs
{
    public const string Device = "VMC5AX";
    public const long InstanceA = 1000;
    public const long InstanceB = 2000;
    public const long BufferSize = 131072;

    public static DevicesDocument Devices(long instanceId = InstanceA) =>
        new(instanceId, [Device], new Dictionary<string, DataItemInfo>
        {
            ["xpos"] = new("xpos", "SAMPLE", "POSITION", "ACTUAL", "MILLIMETER"),
        });

    /// <summary>Streams document with "xpos" observations of the given sequences (any order).</summary>
    public static StreamsDocument Streams(long instanceId, long next, long last, params long[] sequences) =>
        StreamsOf(instanceId, next, last, sequences.Select(s => Observation(s, instanceId)).ToArray());

    public static StreamsDocument StreamsOf(long instanceId, long next, long last, params MTConnectObservation[] observations) =>
        new(new StreamsHeader(instanceId, next, 1, last, BufferSize), observations.OrderBy(o => o.Sequence).ToList());

    public static MTConnectObservation Observation(long sequence, long instanceId = InstanceA, string dataItemId = "xpos", string device = Device) =>
        new(device, instanceId, dataItemId, sequence, new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence),
            ObservationCategory.Sample, "POSITION", "ACTUAL", null, "1.0", null, null);
}
