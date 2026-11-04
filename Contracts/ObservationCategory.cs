using System.Text.Json.Serialization;

namespace DataAcquisition.Contracts;

/// <summary>MTConnect data item category.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObservationCategory>))]
public enum ObservationCategory
{
    Sample,
    Event,
    Condition,
}
