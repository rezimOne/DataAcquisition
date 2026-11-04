using DataAcquisition.Worker.MTConnect;

namespace DataAcquisition.Worker
{
    /// <summary>Runs MTConnect synchronization until the host stops.</summary>
    public class Worker(MTConnectSynchronizer synchronizer) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => synchronizer.RunAsync(stoppingToken);
    }
}
