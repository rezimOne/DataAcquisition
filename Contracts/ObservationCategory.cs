using System.Text.Json.Serialization;

namespace DataAcquisition.Contracts;

/// <summary>MTConnect data item category.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ObservationCategory>))]
public enum ObservationCategory
{
    Sample,
    Event,
    Condition,
}
