namespace DataAcquisition.Contracts;

/// <summary>
/// Batch of observations from one device and one agent instance (MTConnect <c>Header/@instanceId</c>).
/// Fields are nullable so the API can report missing values as validation errors.
/// </summary>
public sealed record ObservationBatchRequest(
    string? DeviceId,
    long? InstanceId,
    IReadOnlyList<ObservationDto?>? Observations);

/// <summary>Single MTConnect observation as sent by the worker.</summary>
public sealed record ObservationDto(
    string? DataItemId,
    long? Sequence,
    DateTimeOffset? Timestamp,
    ObservationCategory? Category,
    string? Type,
    string? SubType,
    string? Value,
    ConditionLevel? ConditionLevel)
{
    // Added after the first release as init properties (not constructor parameters), so existing callers
    // stay source- and binary-compatible and older payloads without these fields remain valid.

    /// <summary>Units from the device model (MTConnect /probe), e.g. MILLIMETER. Optional.</summary>
    public string? Units { get; init; }

    /// <summary>Native alarm code of a CONDITION observation, e.g. AL-0123. Optional.</summary>
    public string? NativeCode { get; init; }
}
