using System.Net;
using System.Net.Http.Json;
using DataAcquisition.Contracts;
using Microsoft.Extensions.Options;
using Xunit;
using static DataAcquisition.Api.Tests.TestData;

namespace DataAcquisition.Api.Tests;

public sealed class HealthAndStartupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // AC-07

    [Fact]
    public async Task GetHealth_WithoutApiKey_Returns200Healthy()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    // AC-08

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-key-31-characters-long-xx")]
    public async Task Startup_MissingOrTooShortApiKey_FailsWithConfigurationKeyInMessage(string? apiKey)
    {
        await using var factory = new ApiFactory(apiKey);

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(exception);
        Assert.Contains("ApiKey:Value", validation.Message);
        if (!string.IsNullOrEmpty(apiKey))
        {
            Assert.DoesNotContain(apiKey, validation.Message);
        }
    }

    [Fact]
    public async Task Startup_ApiKeyOfMinimumLength_Starts()
    {
        await using var factory = new ApiFactory(new string('k', 32));

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Requests_WithValidAndInvalidKeys_NeverLogKeyValues()
    {
        const string wrongKey = "wrong-key-that-must-not-be-logged";
        await using var factory = new ApiFactory();
        using var authorized = factory.CreateAuthorizedClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Add(ApiContract.ApiKeyHeader, wrongKey);

        await authorized.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1)), Ct);
        await authorized.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1, value: "1.0")), Ct);
        await wrong.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(2)), Ct);
        await wrong.GetAsync($"{ApiContract.ObservationsPath}?deviceId={DeviceId}", Ct);

        var records = factory.Logs.GetSnapshot();
        Assert.Contains(records, r => r.Message.Contains("missing or invalid API key"));
        Assert.Contains(records, r => r.Message.Contains("1 accepted"));
        Assert.All(records, r =>
        {
            var text = r.Message + r.Exception;
            Assert.DoesNotContain(ApiFactory.TestApiKey, text);
            Assert.DoesNotContain(wrongKey, text);
        });
    }
}
