using DataAcquisition.Contracts;

namespace DataAcquisition.Api.Tests;

internal static class TestData
{
    public const string DeviceId = "VMC5AX";
    public const long InstanceId = 1793790000;
    public static readonly DateTimeOffset BaseTime = new(2026, 11, 4, 10, 15, 30, TimeSpan.Zero);

    public static ObservationDto Sample(long sequence, string dataItemId = "xpos", string value = "112.457") =>
        new(dataItemId, sequence, BaseTime.AddMilliseconds(200 * sequence), ObservationCategory.Sample,
            "POSITION", "ACTUAL", value, null);

    public static ObservationDto Condition(long sequence) =>
        new("system", sequence, BaseTime.AddMilliseconds(200 * sequence), ObservationCategory.Condition,
            "SYSTEM", null, "SPINDLE OVERLOAD", ConditionLevel.Fault);

    public static ObservationBatchRequest Batch(params ObservationDto?[] observations) =>
        Batch(DeviceId, InstanceId, observations);

    public static ObservationBatchRequest Batch(string deviceId, long instanceId, params ObservationDto?[] observations) =>
        new(deviceId, instanceId, observations);

    public static StoredObservationDto Stored(long sequence, string deviceId = DeviceId, long instanceId = InstanceId,
        string value = "112.457") =>
        new(deviceId, instanceId, "xpos", sequence, BaseTime.AddMilliseconds(200 * sequence), ObservationCategory.Sample,
            "POSITION", "ACTUAL", value, null);
}
