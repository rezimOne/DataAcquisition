using DataAcquisition.Contracts;
using DataAcquisition.Worker.MTConnect;
using ContractCategory = DataAcquisition.Contracts.ObservationCategory;
using ContractConditionLevel = DataAcquisition.Contracts.ConditionLevel;
using WorkerCategory = DataAcquisition.Worker.MTConnect.ObservationCategory;
using WorkerConditionLevel = DataAcquisition.Worker.MTConnect.ConditionLevel;

namespace DataAcquisition.Worker.Api;

/// <summary>Explicit mapping of the worker model to the API contract (no reflection).</summary>
internal static class ObservationBatchMapping
{
    public static ObservationBatchRequest ToRequest(this ObservationBatch batch) =>
        new(batch.DeviceName, batch.InstanceId, batch.Observations.Select(ToDto).ToList());

    private static ObservationDto? ToDto(MTConnectObservation observation) =>
        new(
            observation.DataItemId,
            observation.Sequence,
            observation.Timestamp,
            ToContract(observation.Category),
            observation.Type,
            observation.SubType,
            observation.Value,
            observation.ConditionLevel is { } level ? ToContract(level) : null)
        {
            Units = observation.Units,
            NativeCode = observation.NativeCode,
        };

    private static ContractCategory ToContract(WorkerCategory category) => category switch
    {
        WorkerCategory.Sample => ContractCategory.Sample,
        WorkerCategory.Event => ContractCategory.Event,
        WorkerCategory.Condition => ContractCategory.Condition,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown observation category."),
    };

    private static ContractConditionLevel ToContract(WorkerConditionLevel level) => level switch
    {
        WorkerConditionLevel.Normal => ContractConditionLevel.Normal,
        WorkerConditionLevel.Warning => ContractConditionLevel.Warning,
        WorkerConditionLevel.Fault => ContractConditionLevel.Fault,
        WorkerConditionLevel.Unavailable => ContractConditionLevel.Unavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown condition level."),
    };
}
