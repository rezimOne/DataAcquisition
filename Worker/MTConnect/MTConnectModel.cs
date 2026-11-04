namespace DataAcquisition.Worker.MTConnect;

public enum ObservationCategory
{
    Sample,
    Event,
    Condition,
}

public enum ConditionLevel
{
    Normal,
    Warning,
    Fault,
    Unavailable,
}

public enum ObservationSource
{
    Current,
    Sample,
}

/// <summary>Single MTConnect observation; timestamp in UTC. Type/SubType/Units come from /probe when known.</summary>
public sealed record MTConnectObservation(
    string DeviceName,
    long InstanceId,
    string DataItemId,
    long Sequence,
    DateTimeOffset Timestamp,
    ObservationCategory Category,
    string? Type,
    string? SubType,
    string? Units,
    string Value,
    ConditionLevel? ConditionLevel,
    string? NativeCode);

public sealed record StreamsHeader(long InstanceId, long NextSequence, long FirstSequence, long LastSequence, long BufferSize)
{
    /// <summary>Agent's circular buffer is full, so FirstSequence moves forward with every new observation.</summary>
    public bool IsBufferFull => LastSequence - FirstSequence + 1 >= BufferSize;
}

/// <summary>MTConnectStreams document; observations sorted by sequence.</summary>
public sealed record StreamsDocument(StreamsHeader Header, IReadOnlyList<MTConnectObservation> Observations);

public sealed record DataItemInfo(string Id, string Category, string Type, string? SubType, string? Units);

/// <summary>MTConnectDevices document (/probe).</summary>
public sealed record DevicesDocument(
    long InstanceId,
    IReadOnlyList<string> DeviceNames,
    IReadOnlyDictionary<string, DataItemInfo> DataItems);

/// <summary>Observations of one device from one agent response, sorted by sequence.</summary>
public sealed record ObservationBatch(
    string DeviceName,
    long InstanceId,
    ObservationSource Source,
    IReadOnlyList<MTConnectObservation> Observations);

/// <summary>Response is not a supported MTConnect 1.3 document (bad XML, unknown root or namespace, missing attributes).</summary>
public sealed class MTConnectFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Agent answered with an MTConnectError document, e.g. OUT_OF_RANGE.</summary>
public sealed class MTConnectAgentErrorException(string errorCode, string message)
    : Exception($"MTConnect agent error {errorCode}: {message}")
{
    public const string OutOfRange = "OUT_OF_RANGE";

    public string ErrorCode { get; } = errorCode;
}
