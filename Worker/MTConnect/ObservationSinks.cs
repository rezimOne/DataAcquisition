namespace DataAcquisition.Worker.MTConnect;

/// <summary>Receives observation batches. An exception means "not delivered": the batch will be fetched and delivered again.</summary>
public interface IObservationSink
{
    Task ReceiveAsync(ObservationBatch batch, CancellationToken cancellationToken);
}
