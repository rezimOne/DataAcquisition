using DataAcquisition.Contracts;

namespace DataAcquisition.Api.Observations;

/// <summary>Observation storage. Key: device + agent instance + sequence; first write wins.</summary>
public interface IObservationStore
{
    Task<ObservationBatchResponse> AddAsync(IReadOnlyList<StoredObservationDto> observations, CancellationToken cancellationToken);

    Task<ObservationQueryResponse> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}

/// <summary>Observations of one device with timestamp in [From, To), ordered by instance and sequence.</summary>
public sealed record ObservationQuery(string DeviceId, DateTimeOffset? From, DateTimeOffset? To, int Limit);
