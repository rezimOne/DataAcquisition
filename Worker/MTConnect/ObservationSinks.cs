namespace DataAcquisition.Worker.MTConnect;

/// <summary>Receives observation batches. An exception means "not delivered": the batch will be fetched and delivered again.</summary>
public interface IObservationSink
{
    Task ReceiveAsync(ObservationBatch batch, CancellationToken cancellationToken);
}

/// <summary>DA-002 sink: logs batch statistics only (no values). Replaced by the API sender in DA-003.</summary>
internal sealed class LoggingObservationSink(ILogger<LoggingObservationSink> logger) : IObservationSink
{
    public Task ReceiveAsync(ObservationBatch batch, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Received {Count} observations from {DeviceName} (instance {InstanceId}, {Source}), sequence {FirstSequence}-{LastSequence}.",
            batch.Observations.Count, batch.DeviceName, batch.InstanceId, batch.Source,
            batch.Observations[0].Sequence, batch.Observations[^1].Sequence);
        return Task.CompletedTask;
    }
}
