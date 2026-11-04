using Microsoft.Extensions.Options;

namespace DataAcquisition.Api.ApiKey;

/// <summary>API key expected in the X-Api-Key header. Configure via User Secrets or ApiKey__Value, never appsettings.</summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";
    public const int MinimumLength = 32;

    public string? Value { get; set; }
}

internal sealed class ApiKeyOptionsValidator : IValidateOptions<ApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiKeyOptions options) =>
        options.Value is { } value && !string.IsNullOrWhiteSpace(value) && value.Length >= ApiKeyOptions.MinimumLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Configuration '{ApiKeyOptions.SectionName}:Value' is required and must be at least {ApiKeyOptions.MinimumLength} characters long.");
}
