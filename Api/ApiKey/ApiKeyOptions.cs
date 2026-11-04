using Microsoft.Extensions.Options;

namespace DataAcquisition.Api.ApiKey;

/// <summary>
/// API key expected in the X-Api-Key header. Configure via User Secrets (DataAcquisition:Api:ApiKey)
/// or environment variable DataAcquisition__Api__ApiKey, never appsettings.
/// </summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "DataAcquisition:Api";
    public const string ConfigurationKey = SectionName + ":" + nameof(ApiKey);
    public const int MinimumLength = 32;

    public string? ApiKey { get; set; }
}

internal sealed class ApiKeyOptionsValidator : IValidateOptions<ApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiKeyOptions options) =>
        options.ApiKey is { } apiKey && !string.IsNullOrWhiteSpace(apiKey) && apiKey.Length >= ApiKeyOptions.MinimumLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Configuration '{ApiKeyOptions.ConfigurationKey}' is required and must be at least {ApiKeyOptions.MinimumLength} characters long.");
}
