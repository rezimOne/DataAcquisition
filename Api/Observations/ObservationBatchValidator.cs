using DataAcquisition.Contracts;

namespace DataAcquisition.Api.Observations;

/// <summary>Validates a whole batch; error keys are camelCase field paths, e.g. observations[3].dataItemId.</summary>
internal static class ObservationBatchValidator
{
    public const int MaxIdLength = 64;
    public const int MaxValueLength = 1024;

    public static Dictionary<string, string[]> Validate(ObservationBatchRequest request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages))
            {
                errors[key] = messages = [];
            }
            messages.Add(message);
        }

        ValidateId(request.DeviceId, "deviceId", Add);
        if (request.InstanceId is not > 0)
        {
            Add("instanceId", "instanceId is required and must be greater than 0.");
        }

        if (request.Observations is not { Count: > 0 and <= ApiContract.MaxBatchSize } observations)
        {
            Add("observations", $"observations must contain between 1 and {ApiContract.MaxBatchSize} items.");
            return ToResult(errors);
        }

        var sequences = new HashSet<long>();
        for (var i = 0; i < observations.Count; i++)
        {
            var path = $"observations[{i}]";
            if (observations[i] is not { } observation)
            {
                Add(path, "Observation is required.");
                continue;
            }

            ValidateId(observation.DataItemId, $"{path}.dataItemId", Add);

            if (observation.Sequence is not >= 1)
            {
                Add($"{path}.sequence", "sequence is required and must be at least 1.");
            }
            else if (!sequences.Add(observation.Sequence.Value))
            {
                Add($"{path}.sequence", $"Duplicate sequence {observation.Sequence} in batch.");
            }

            if (observation.Timestamp is null)
            {
                Add($"{path}.timestamp", "timestamp is required.");
            }

            if (observation.Category is null)
            {
                Add($"{path}.category", "category is required.");
            }
            else if (!Enum.IsDefined(observation.Category.Value))
            {
                Add($"{path}.category", "category must be one of: Sample, Event, Condition.");
            }

            ValidateOptionalLength(observation.Type, $"{path}.type", MaxIdLength, Add);
            ValidateOptionalLength(observation.SubType, $"{path}.subType", MaxIdLength, Add);

            if (observation.Value is null)
            {
                Add($"{path}.value", "value is required.");
            }
            else
            {
                ValidateOptionalLength(observation.Value, $"{path}.value", MaxValueLength, Add);
            }

            if (observation.Category == ObservationCategory.Condition && observation.ConditionLevel is null)
            {
                Add($"{path}.conditionLevel", "conditionLevel is required for Condition observations.");
            }
            else if (observation.Category is ObservationCategory.Sample or ObservationCategory.Event
                     && observation.ConditionLevel is not null)
            {
                Add($"{path}.conditionLevel", "conditionLevel is allowed only for Condition observations.");
            }
            else if (observation.ConditionLevel is { } level && !Enum.IsDefined(level))
            {
                Add($"{path}.conditionLevel", "conditionLevel must be one of: Normal, Warning, Fault, Unavailable.");
            }
        }

        return ToResult(errors);
    }

    private static void ValidateId(string? value, string key, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            add(key, $"{key} is required.");
        }
        else
        {
            ValidateOptionalLength(value, key, MaxIdLength, add);
        }
    }

    private static void ValidateOptionalLength(string? value, string key, int maxLength, Action<string, string> add)
    {
        if (value is { Length: var length } && length > maxLength)
        {
            add(key, $"{key} must be at most {maxLength} characters long.");
        }
    }

    private static Dictionary<string, string[]> ToResult(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
