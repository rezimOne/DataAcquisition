namespace DataAcquisition.Worker.MTConnect;

/// <summary>MTConnect agent endpoints used by the synchronizer.</summary>
public interface IMTConnectAgent
{
    Task<DevicesDocument> ProbeAsync(CancellationToken cancellationToken);

    Task<StreamsDocument> CurrentAsync(CancellationToken cancellationToken);

    /// <exception cref="MTConnectAgentErrorException">Agent error document, e.g. OUT_OF_RANGE.</exception>
    Task<StreamsDocument> SampleAsync(long from, int count, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP client of the MTConnect agent. Creates a named client per request (IHttpClientFactory),
/// so the singleton synchronizer does not hold a single HttpClient for the process lifetime.
/// </summary>
internal sealed class MTConnectClient(IHttpClientFactory httpClientFactory) : IMTConnectAgent
{
    public const string HttpClientName = "MTConnectAgent";

    public async Task<DevicesDocument> ProbeAsync(CancellationToken cancellationToken) =>
        MTConnectParser.ParseDevices(await GetAsync("probe", cancellationToken));

    public async Task<StreamsDocument> CurrentAsync(CancellationToken cancellationToken) =>
        MTConnectParser.ParseStreams(await GetAsync("current", cancellationToken));

    public async Task<StreamsDocument> SampleAsync(long from, int count, CancellationToken cancellationToken) =>
        MTConnectParser.ParseStreams(await GetAsync($"sample?from={from}&count={count}", cancellationToken));

    private async Task<string> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(relativeUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw MTConnectParser.TryParseError(body) is { } agentError
            ? agentError
            : new HttpRequestException(
                $"MTConnect agent returned HTTP {(int)response.StatusCode} for '{relativeUrl}'.", null, response.StatusCode);
    }
}
