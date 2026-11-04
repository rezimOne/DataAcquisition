using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.MTConnect;

/// <summary>
/// Keeps the worker in sync with the agent: /probe -> /current -> /sample?from=nextSequence.
/// Agent restart (new instanceId) re-probes; OUT_OF_RANGE re-reads /current; other failures are retried with backoff.
/// Delivery is at-least-once: nextSequence moves only after the sink accepted the batch.
/// </summary>
public sealed class MTConnectSynchronizer(
    IMTConnectAgent agent,
    IObservationSink sink,
    IOptions<MTConnectOptions> options,
    TimeProvider timeProvider,
    ILogger<MTConnectSynchronizer> logger)
{
    private readonly MTConnectOptions _options = options.Value;
    private readonly HashSet<string> _unknownDataItems = [];

    private DevicesDocument? _devices;
    private bool _synchronized;
    private long? _instanceId;
    private long _nextSequence;
    private long? _lostFromSequence;
    private TimeSpan? _retryDelay;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = await StepAsync(stoppingToken);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping.
        }
    }

    /// <summary>One cycle with error handling; returns the delay before the next cycle.</summary>
    internal async Task<TimeSpan> StepAsync(CancellationToken cancellationToken)
    {
        try
        {
            var delay = await CycleAsync(cancellationToken);
            _retryDelay = null;
            return delay;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var delay = _retryDelay ?? _options.RetryInitialDelay;
            _retryDelay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.RetryMaxDelay.Ticks));
            // Message only: agent responses and observation values are not logged.
            logger.LogWarning(
                "MTConnect agent {AgentUrl} cycle failed ({ErrorType}: {ErrorMessage}); retrying in {RetryDelay}.",
                _options.AgentUrl, ex.GetType().Name, ex.Message, delay);
            return delay;
        }
    }

    private async Task<TimeSpan> CycleAsync(CancellationToken cancellationToken)
    {
        if (!_synchronized)
        {
            await SynchronizeAsync(cancellationToken);
            return _options.PollInterval;
        }

        StreamsDocument sample;
        try
        {
            sample = await agent.SampleAsync(_nextSequence, _options.SampleCount, cancellationToken);
        }
        catch (MTConnectAgentErrorException ex) when (ex.ErrorCode == MTConnectAgentErrorException.OutOfRange)
        {
            _lostFromSequence ??= _nextSequence;
            _synchronized = false;
            await SynchronizeAsync(cancellationToken);
            return _options.PollInterval;
        }

        if (sample.Header.InstanceId != _instanceId)
        {
            logger.LogInformation(
                "MTConnect agent restarted (instance {OldInstanceId} -> {NewInstanceId}); resynchronizing.",
                _instanceId, sample.Header.InstanceId);
            _synchronized = false;
            _devices = null;
            await SynchronizeAsync(cancellationToken);
            return _options.PollInterval;
        }

        await DeliverAsync(sample, ObservationSource.Sample, cancellationToken);
        _nextSequence = sample.Header.NextSequence;
        return sample.Header.NextSequence <= sample.Header.LastSequence ? TimeSpan.Zero : _options.PollInterval;
    }

    private async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        if (_devices is null)
        {
            _devices = await agent.ProbeAsync(cancellationToken);
            _unknownDataItems.Clear();
            logger.LogInformation(
                "MTConnect agent {AgentUrl} (instance {InstanceId}): {DeviceCount} device(s), {DataItemCount} data items.",
                _options.AgentUrl, _devices.InstanceId, _devices.DeviceNames.Count, _devices.DataItems.Count);
        }

        var current = await agent.CurrentAsync(cancellationToken);

        if (_lostFromSequence is { } lostFrom && current.Header.InstanceId == _instanceId && lostFrom < current.Header.FirstSequence)
        {
            logger.LogWarning(
                "Observations {LostFrom}-{LostTo} of instance {InstanceId} left the agent buffer before they were read and are lost.",
                lostFrom, current.Header.FirstSequence - 1, current.Header.InstanceId);
        }

        await DeliverAsync(current, ObservationSource.Current, cancellationToken);

        _lostFromSequence = null;
        _instanceId = current.Header.InstanceId;
        _nextSequence = current.Header.NextSequence;
        _synchronized = true;
        logger.LogInformation(
            "Synchronized with MTConnect agent instance {InstanceId} at sequence {NextSequence}.",
            _instanceId, _nextSequence);
    }

    private async Task DeliverAsync(StreamsDocument document, ObservationSource source, CancellationToken cancellationToken)
    {
        foreach (var device in document.Observations.GroupBy(o => o.DeviceName))
        {
            var observations = device.Select(Enrich).ToList();
            await sink.ReceiveAsync(
                new ObservationBatch(device.Key, document.Header.InstanceId, source, observations), cancellationToken);
        }
    }

    private MTConnectObservation Enrich(MTConnectObservation observation)
    {
        if (_devices?.DataItems.TryGetValue(observation.DataItemId, out var info) == true)
        {
            return observation with { Type = info.Type, SubType = info.SubType, Units = info.Units };
        }

        if (_unknownDataItems.Add(observation.DataItemId))
        {
            logger.LogWarning(
                "Data item {DataItemId} is not described by /probe; using the type from the stream element.",
                observation.DataItemId);
        }
        return observation;
    }
}
