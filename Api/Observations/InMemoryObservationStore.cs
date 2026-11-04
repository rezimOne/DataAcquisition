using System.Collections.Concurrent;
using DataAcquisition.Contracts;

namespace DataAcquisition.Api.Observations;

/// <summary>
/// Thread-safe in-memory store for development. Data is lost on restart and is not bounded
/// (accepted in DA-001; persistent storage is a separate topic).
/// </summary>
internal sealed class InMemoryObservationStore(ILogger<InMemoryObservationStore> logger) : IObservationStore
{
    private readonly ConcurrentDictionary<ObservationKey, StoredObservationDto> _observations = new();

    public Task<ObservationBatchResponse> AddAsync(IReadOnlyList<StoredObservationDto> observations, CancellationToken cancellationToken)
    {
        var accepted = 0;
        var duplicates = 0;
        foreach (var observation in observations)
        {
            var key = new ObservationKey(observation.DeviceId, observation.InstanceId, observation.Sequence);
            if (_observations.TryAdd(key, observation))
            {
                accepted++;
                continue;
            }

            duplicates++;
            if (_observations.TryGetValue(key, out var existing) && existing != observation)
            {
                // Values are not logged: they are machine data, not diagnostics.
                logger.LogWarning(
                    "Duplicate observation {DeviceId}/{InstanceId}/{Sequence} differs from the stored one; keeping the stored one.",
                    key.DeviceId, key.InstanceId, key.Sequence);
            }
        }

        return Task.FromResult(new ObservationBatchResponse(accepted, duplicates));
    }

    public Task<ObservationQueryResponse> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        var matches = _observations.Values
            .Where(o => o.DeviceId == query.DeviceId
                        && (query.From is null || o.Timestamp >= query.From)
                        && (query.To is null || o.Timestamp < query.To))
            .OrderBy(o => o.InstanceId)
            .ThenBy(o => o.Sequence)
            .Take(query.Limit + 1)
            .ToList();

        var truncated = matches.Count > query.Limit;
        if (truncated)
        {
            matches.RemoveAt(matches.Count - 1);
        }

        return Task.FromResult(new ObservationQueryResponse(matches, truncated));
    }

    private readonly record struct ObservationKey(string DeviceId, long InstanceId, long Sequence);
}
