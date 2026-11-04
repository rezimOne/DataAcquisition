using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.MTConnect;

/// <summary>MTConnect agent polling settings (DataAcquisition:MTConnect). Defaults match the local simulator.</summary>
public sealed class MTConnectOptions
{
    public const string SectionName = "DataAcquisition:MTConnect";

    public Uri AgentUrl { get; set; } = new("http://localhost:5000");

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Max observations per /sample request; the API (DA-001) accepts at most 1000 per batch.</summary>
    public int SampleCount { get; set; } = 1000;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class MTConnectOptionsValidator : IValidateOptions<MTConnectOptions>
{
    public ValidateOptionsResult Validate(string? name, MTConnectOptions options)
    {
        var failures = new List<string>();

        if (options.AgentUrl is not { IsAbsoluteUri: true } url || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{Key(nameof(options.AgentUrl))} must be an absolute http or https URL.");
        }
        CheckRange(failures, nameof(options.PollInterval), options.PollInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1));
        if (options.SampleCount is < 1 or > 1000)
        {
            failures.Add($"{Key(nameof(options.SampleCount))} must be between 1 and 1000.");
        }
        CheckRange(failures, nameof(options.RequestTimeout), options.RequestTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
        CheckRange(failures, nameof(options.RetryInitialDelay), options.RetryInitialDelay, TimeSpan.FromMilliseconds(100), options.RetryMaxDelay);
        CheckRange(failures, nameof(options.RetryMaxDelay), options.RetryMaxDelay, TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string property, TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{Key(property)} must be between {min} and {max}.");
        }
    }

    private static string Key(string property) => $"{MTConnectOptions.SectionName}:{property}";
}
