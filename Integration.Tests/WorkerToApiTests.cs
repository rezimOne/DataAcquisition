using System.Net;
using System.Net.Http.Json;
using DataAcquisition.Contracts;
using DataAcquisition.Worker.Api;
using DataAcquisition.Worker.MTConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using ApiKeyOptions = DataAcquisition.Api.ApiKey.ApiKeyOptions;
using WorkerCategory = DataAcquisition.Worker.MTConnect.ObservationCategory;
using WorkerConditionLevel = DataAcquisition.Worker.MTConnect.ConditionLevel;

namespace DataAcquisition.Integration.Tests;

/// <summary>
/// Worker sink against the real API in-process (TestServer, no ports). The factory is typed with a class from
/// the Api assembly, not Program: both Api and Worker have a top-level Program.
/// </summary>
public sealed class WorkerToApiTests : IAsyncDisposable
{
    private const string Device = "VMC5AX";
    private const long Instance = 1793790000;

    /// <summary>Random per test run, so no key literal lives in the repository.</summary>
    private static readonly string ApiKey = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private static readonly DateTimeOffset Time = new(2026, 11, 4, 10, 15, 30, 120, TimeSpan.Zero);

    private readonly ApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    // AC-02

    [Fact]
    public async Task Sink_SendsBatch_ApiReturnsSameObservationsWithUnitsAndNativeCode()
    {
        var sink = CreateSink(_api.Server.CreateHandler());
        var batch = Batch(ObservationSource.Sample,
            Sample(41, "112.457"),
            new MTConnectObservation(Device, Instance, "system", 42, Time, WorkerCategory.Condition,
                "SYSTEM", null, null, "SPINDLE OVERLOAD", WorkerConditionLevel.Fault, "AL-0123"));

        await sink.ReceiveAsync(batch, Ct);

        var items = await QueryAsync();
        Assert.Equal(
            [
                new StoredObservationDto(Device, Instance, "xpos", 41, Time, Contracts.ObservationCategory.Sample,
                    "POSITION", "ACTUAL", "112.457", null) { Units = "MILLIMETER" },
                new StoredObservationDto(Device, Instance, "system", 42, Time, Contracts.ObservationCategory.Condition,
                    "SYSTEM", null, "SPINDLE OVERLOAD", Contracts.ConditionLevel.Fault) { NativeCode = "AL-0123" },
            ],
            items);
    }

    // AC-03

    [Fact]
    public async Task Synchronizer_ApiUnavailableForTwoRequests_RetriesAndStoresEveryObservationOnce()
    {
        var outage = new OutageHandler(failures: 2) { InnerHandler = _api.Server.CreateHandler() };
        var agent = new ScriptedAgent();
        agent.Probes.Enqueue(Devices());
        // The current snapshot is fetched again after each failed delivery (resync without /probe).
        for (var i = 0; i < 3; i++)
        {
            agent.Currents.Enqueue(Streams(next: 4, last: 3, Sample(1, "1.0"), Sample(2, "2.0"), Sample(3, "3.0")));
        }
        agent.Samples.Enqueue(Streams(next: 6, last: 5, Sample(4, "4.0"), Sample(5, "5.0")));
        var synchronizer = new MTConnectSynchronizer(agent, CreateSink(outage), Options.Create(new MTConnectOptions()),
            new FakeTimeProvider(), NullLogger<MTConnectSynchronizer>.Instance);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            delays.Add(await synchronizer.StepAsync(Ct));
        }

        Assert.Equal([1, 2, 0, 1], delays.Select(d => d.TotalSeconds));
        Assert.Equal(2, outage.RejectedRequests);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], (await QueryAsync()).Select(o => o.Sequence));
    }

    private IObservationSink CreateSink(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAcquisition:Api:BaseUrl"] = _api.Server.BaseAddress.ToString(),
                ["DataAcquisition:Api:ApiKey"] = ApiKey,
            })
            .Build());
        services.AddLogging();
        services.AddDataAcquisitionApi();
        services.AddHttpClient(ApiObservationSink.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IObservationSink>();
    }

    private async Task<IReadOnlyList<StoredObservationDto>> QueryAsync()
    {
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add(ApiContract.ApiKeyHeader, ApiKey);
        var response = await client.GetFromJsonAsync<ObservationQueryResponse>($"{ApiContract.ObservationsPath}?deviceId={Device}", Ct);
        return response!.Items;
    }

    private static MTConnectObservation Sample(long sequence, string value) =>
        new(Device, Instance, "xpos", sequence, Time, WorkerCategory.Sample, "POSITION", "ACTUAL", "MILLIMETER", value, null, null);

    private static ObservationBatch Batch(ObservationSource source, params MTConnectObservation[] observations) =>
        new(Device, Instance, source, observations);

    private static DevicesDocument Devices() =>
        new(Instance, [Device], new Dictionary<string, DataItemInfo>
        {
            ["xpos"] = new("xpos", "SAMPLE", "POSITION", "ACTUAL", "MILLIMETER"),
        });

    private static StreamsDocument Streams(long next, long last, params MTConnectObservation[] observations) =>
        new(new StreamsHeader(Instance, next, 1, last, 131_072), observations);

    private sealed class ApiFactory : WebApplicationFactory<ApiKeyOptions>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { [ApiKeyOptions.ConfigurationKey] = ApiKey }));
        }
    }

    /// <summary>Simulates an API outage: answers 503 to the first N requests, then forwards to the real API.</summary>
    private sealed class OutageHandler(int failures) : DelegatingHandler
    {
        private int _remainingFailures = failures;

        public int RejectedRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                RejectedRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Agent returning queued documents in order.</summary>
    private sealed class ScriptedAgent : IMTConnectAgent
    {
        public Queue<DevicesDocument> Probes { get; } = new();

        public Queue<StreamsDocument> Currents { get; } = new();

        public Queue<StreamsDocument> Samples { get; } = new();

        public Task<DevicesDocument> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Probes.Dequeue());

        public Task<StreamsDocument> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Currents.Dequeue());

        public Task<StreamsDocument> SampleAsync(long from, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Samples.Dequeue());
    }
}
