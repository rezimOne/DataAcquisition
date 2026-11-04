using DataAcquisition.Api.ApiKey;
using DataAcquisition.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace DataAcquisition.Api.Tests;

/// <summary>
/// In-process API host. "Testing" environment, so User Secrets of the developer are not loaded;
/// the API key is set explicitly (overrides environment variables).
/// </summary>
public sealed class ApiFactory(string? apiKey) : WebApplicationFactory<Program>
{
    /// <summary>Random per test run, so no key literal lives in the repository.</summary>
    public static readonly string TestApiKey = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    public ApiFactory() : this(TestApiKey)
    {
    }

    public FakeLogCollector Logs => Services.GetFakeLogCollector();

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiContract.ApiKeyHeader, TestApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?> { [ApiKeyOptions.ConfigurationKey] = apiKey }));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
    }
}
