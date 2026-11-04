using System.Text.Json.Serialization;

namespace DataAcquisition.Contracts;

/// <summary>Enum as a string only: numbers (7) and numeric strings ("7") are rejected during deserialization.</summary>
public sealed class StrictStringEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(namingPolicy: null, allowIntegerValues: false)
    where TEnum : struct, Enum;
