using System.Text.Json.Serialization;

namespace DataAcquisition.Contracts;

/// <summary>Level of an MTConnect CONDITION observation.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ConditionLevel>))]
public enum ConditionLevel
{
    Normal,
    Warning,
    Fault,
    Unavailable,
}
