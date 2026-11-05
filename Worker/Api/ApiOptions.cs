using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.Api;

/// <summary>
/// Data acquisition API client settings (DataAcquisition:Api). Same section and key name as in the API project,
/// so one value configures both. The key comes from User Secrets or DataAcquisition__Api__ApiKey, never appsettings.
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "DataAcquisition:Api";
    public const int MinimumApiKeyLength = 32;

    public Uri BaseUrl { get; set; } = new("http://localhost:5100");

    public string? ApiKey { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

internal sealed class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        var failures = new List<string>();

        if (options.BaseUrl is not { IsAbsoluteUri: true } url || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{Key(nameof(options.BaseUrl))} must be an absolute http or https URL.");
        }
        if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length < ApiOptions.MinimumApiKeyLength)
        {
            // Never include the configured value in the message.
            failures.Add($"{Key(nameof(options.ApiKey))} is required and must be at least {ApiOptions.MinimumApiKeyLength} characters long.");
        }
        if (options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add($"{Key(nameof(options.RequestTimeout))} must be between {TimeSpan.FromSeconds(1)} and {TimeSpan.FromMinutes(5)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Key(string property) => $"{ApiOptions.SectionName}:{property}";
}
