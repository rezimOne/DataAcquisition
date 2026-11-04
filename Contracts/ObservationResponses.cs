namespace DataAcquisition.Contracts;

/// <summary>Result of storing a batch: new observations and skipped duplicates.</summary>
public sealed record ObservationBatchResponse(int Accepted, int Duplicates);

/// <summary>Stored observation; timestamp is always UTC.</summary>
public sealed record StoredObservationDto(
    string DeviceId,
    long InstanceId,
    string DataItemId,
    long Sequence,
    DateTimeOffset Timestamp,
    ObservationCategory Category,
    string? Type,
    string? SubType,
    string Value,
    ConditionLevel? ConditionLevel)
{
    /// <summary>Units from the device model, when the client sent them.</summary>
    public string? Units { get; init; }

    /// <summary>Native alarm code of a CONDITION observation, when the client sent it.</summary>
    public string? NativeCode { get; init; }
}

/// <summary>Query result; <see cref="Truncated"/> is true when more observations match than were returned.</summary>
public sealed record ObservationQueryResponse(IReadOnlyList<StoredObservationDto> Items, bool Truncated);
