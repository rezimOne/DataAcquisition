using System.Net;
using System.Text.Json;
using DataAcquisition.Worker.Api;
using DataAcquisition.Worker.MTConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace DataAcquisition.Worker.Tests.Api;

public sealed class ApiObservationSinkTests
{
    private const string SecretValue = "SPINDLE OVERLOAD";
    private const string ProblemJson = "application/problem+json";

    /// <summary>Random per test run, so no key literal lives in the repository.</summary>
    private static readonly string ApiKey = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private static readonly DateTimeOffset Time = new(2026, 11, 4, 10, 15, 30, 120, TimeSpan.Zero);

    private static readonly ObservationBatch Batch = new("VMC5AX", 1793790000, ObservationSource.Sample,
    [
        new MTConnectObservation("VMC5AX", 1793790000, "xpos", 41, Time, ObservationCategory.Sample,
            "POSITION", "ACTUAL", "MILLIMETER", "112.457", null, null),
        new MTConnectObservation("VMC5AX", 1793790000, "system", 42, Time, ObservationCategory.Condition,
            "SYSTEM", null, null, SecretValue, ConditionLevel.Fault, "AL-0123"),
    ]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // AC-02

    [Fact]
    public async Task ReceiveAsync_Accepted_PostsContractJsonWithApiKeyAndLogsResult()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, """{"accepted":2,"duplicates":0}""");
        var (sink, logs) = CreateSink(handler);

        await sink.ReceiveAsync(Batch, Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "http://localhost:5100/api/v1/observations"), (request.Method, request.Url));
        Assert.Equal(ApiKey, request.ApiKey);
        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        Assert.Equal(("VMC5AX", 1793790000L), (root.GetProperty("deviceId").GetString(), root.GetProperty("instanceId").GetInt64()));
        var sample = root.GetProperty("observations")[0];
        Assert.Equal(
            ("xpos", 41L, "2026-11-04T10:15:30.12+00:00", "Sample", "POSITION", "ACTUAL", "112.457", "MILLIMETER"),
            (sample.GetProperty("dataItemId").GetString(), sample.GetProperty("sequence").GetInt64(),
                sample.GetProperty("timestamp").GetString(), sample.GetProperty("category").GetString(),
                sample.GetProperty("type").GetString(), sample.GetProperty("subType").GetString(),
                sample.GetProperty("value").GetString(), sample.GetProperty("units").GetString()));
        var condition = root.GetProperty("observations")[1];
        Assert.Equal(("Condition", "Fault", "AL-0123"),
            (condition.GetProperty("category").GetString(), condition.GetProperty("conditionLevel").GetString(),
                condition.GetProperty("nativeCode").GetString()));
        var info = Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Contains("2 accepted, 0 duplicates", info.Message);
        AssertNoSecrets(logs);
    }

    [Fact]
    public async Task ReceiveAsync_BaseUrlWithPath_KeepsPath()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, """{"accepted":2,"duplicates":0}""");
        var (sink, _) = CreateSink(handler, new() { ["DataAcquisition:Api:BaseUrl"] = "http://api.local:8080/da" });

        await sink.ReceiveAsync(Batch, Ct);

        Assert.Equal("http://api.local:8080/da/api/v1/observations", Assert.Single(handler.Requests).Url);
    }

    // AC-03

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReceiveAsync_TransientStatus_ThrowsSoBatchIsRetried(HttpStatusCode status)
    {
        var (sink, logs) = CreateSink(StubHandler.Returning(status, "oops"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => sink.ReceiveAsync(Batch, Ct));

        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task ReceiveAsync_ConnectionFailure_Throws()
    {
        var (sink, _) = CreateSink(StubHandler.Throwing(new HttpRequestException("No connection could be made.")));

        await Assert.ThrowsAsync<HttpRequestException>(() => sink.ReceiveAsync(Batch, Ct));
    }

    [Fact]
    public async Task ReceiveAsync_ApiDoesNotAnswer_TimesOutAfterRequestTimeout()
    {
        var (sink, _) = CreateSink(StubHandler.Hanging(), new() { ["DataAcquisition:Api:RequestTimeout"] = "00:00:01" });

        var error = await Record.ExceptionAsync(() => sink.ReceiveAsync(Batch, Ct));

        var canceled = Assert.IsAssignableFrom<TaskCanceledException>(error);
        Assert.IsType<TimeoutException>(canceled.InnerException);
    }

    // AC-04

    [Fact]
    public async Task ReceiveAsync_ValidationProblem_LogsFieldNamesWithoutValuesAndSkipsBatch()
    {
        const string problem = """{"title":"One or more validation errors occurred.","status":400,"errors":{"observations[1].value":["value must be at most 1024 characters long."]}}""";
        var (sink, logs) = CreateSink(StubHandler.Returning(HttpStatusCode.BadRequest, problem, ProblemJson));

        var exception = await Record.ExceptionAsync(() => sink.ReceiveAsync(Batch, Ct));

        Assert.Null(exception);
        var error = Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Error);
        Assert.Contains("observations[1].value", error.Message);
        Assert.Contains("41-42", error.Message);
        AssertNoSecrets(logs);
    }

    [Fact]
    public async Task ReceiveAsync_ApiProblemWithoutFieldErrors_LogsAndSkipsBatch()
    {
        // e.g. malformed JSON rejected by the API itself: still the API's verdict on this batch.
        const string problem = """{"title":"Bad Request","status":400}""";
        var (sink, logs) = CreateSink(StubHandler.Returning(HttpStatusCode.BadRequest, problem, ProblemJson));

        var exception = await Record.ExceptionAsync(() => sink.ReceiveAsync(Batch, Ct));

        Assert.Null(exception);
        Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("The plain HTTP request was sent to HTTPS port", "text/html")]
    [InlineData("""{"error":"bad request"}""", "application/json")]
    public async Task ReceiveAsync_BadRequestNotFromApi_ThrowsSoBatchIsRetried(string body, string mediaType)
    {
        // Review R-01: a 400 without the API's ProblemDetails (proxy, wrong port/scheme) is not a verdict on
        // the batch; skipping it would silently drop every batch, so it is retried like other 4xx.
        var (sink, logs) = CreateSink(StubHandler.Returning(HttpStatusCode.BadRequest, body, mediaType));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => sink.ReceiveAsync(Batch, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ReceiveAsync_ApiKeyRejected_LogsErrorAndThrowsSoBatchIsRetried(HttpStatusCode status)
    {
        var (sink, logs) = CreateSink(StubHandler.Returning(status, ""));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => sink.ReceiveAsync(Batch, Ct));

        Assert.Equal(status, error.StatusCode);
        var logged = Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Error);
        Assert.Contains("API key", logged.Message);
        AssertNoSecrets(logs);
        Assert.DoesNotContain(ApiKey, error.Message);
    }

    private static void AssertNoSecrets(FakeLogCollector logs) =>
        Assert.All(logs.GetSnapshot(), r =>
        {
            var text = r.Message + r.Exception;
            Assert.DoesNotContain(ApiKey, text);
            Assert.DoesNotContain(SecretValue, text);
        });

    private static (IObservationSink Sink, FakeLogCollector Logs) CreateSink(
        HttpMessageHandler handler, Dictionary<string, string?>? settings = null)
    {
        var configuration = new Dictionary<string, string?> { ["DataAcquisition:Api:ApiKey"] = ApiKey };
        foreach (var (key, value) in settings ?? [])
        {
            configuration[key] = value;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddLogging(logging => logging.AddFakeLogging());
        services.AddDataAcquisitionApi();
        services.AddHttpClient(ApiObservationSink.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IObservationSink>(), provider.GetFakeLogCollector());
    }

    private sealed record CapturedRequest(HttpMethod Method, string Url, string? ApiKey, string Body);

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public static StubHandler Returning(HttpStatusCode status, string body, string mediaType = "text/plain") =>
            new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, null, mediaType) }));

        public static StubHandler Throwing(Exception exception) =>
            new(_ => Task.FromException<HttpResponseMessage>(exception));

        public static StubHandler Hanging() => new(async ct =>
        {
            await TestWaits.UntilCanceledAsync(ct);
            throw new InvalidOperationException("unreachable");
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await respond(cancellationToken);
        }
    }
}
