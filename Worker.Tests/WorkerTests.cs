using DataAcquisition.Worker.Api;
using DataAcquisition.Worker.MTConnect;
using DataAcquisition.Worker.Tests.MTConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataAcquisition.Worker.Tests;

/// <summary>Host-level tests: registration from Program.cs, configuration validation, start/stop.</summary>
public sealed class WorkerTests
{
    private const string ApiKeySetting = "DataAcquisition:Api:ApiKey";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("AgentUrl", "ftp://agent:5000", "DataAcquisition:MTConnect:AgentUrl")]
    [InlineData("AgentUrl", "/relative", "DataAcquisition:MTConnect:AgentUrl")]
    [InlineData("SampleCount", "0", "DataAcquisition:MTConnect:SampleCount")]
    [InlineData("SampleCount", "1001", "DataAcquisition:MTConnect:SampleCount")]
    [InlineData("PollInterval", "00:00:00", "DataAcquisition:MTConnect:PollInterval")]
    [InlineData("RequestTimeout", "00:10:00", "DataAcquisition:MTConnect:RequestTimeout")]
    [InlineData("RetryMaxDelay", "00:00:00.500", "DataAcquisition:MTConnect:RetryInitialDelay")]
    public async Task StartAsync_InvalidConfiguration_FailsWithConfigurationKey(string setting, string value, string expectedKey)
    {
        using var host = BuildHost(new Dictionary<string, string?> { [$"DataAcquisition:MTConnect:{setting}"] = value });

        var exception = await Record.ExceptionAsync(() => host.StartAsync(Ct));

        var validation = Assert.IsType<OptionsValidationException>(exception);
        Assert.Contains(expectedKey, validation.Message);
    }

    [Fact]
    public void Options_NoConfiguration_UsesSimulatorDefaults()
    {
        using var host = BuildHost();

        var options = host.Services.GetRequiredService<IOptions<MTConnectOptions>>().Value;

        Assert.Equal(
            (new Uri("http://localhost:5000"), TimeSpan.FromSeconds(1), 1000, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30)),
            (options.AgentUrl, options.PollInterval, options.SampleCount, options.RequestTimeout, options.RetryInitialDelay, options.RetryMaxDelay));
    }

    [Fact]
    public async Task StopAsync_WhileRequestInProgress_CompletesWithoutWarningsOrErrors()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new FakeAgent();
        agent.Probes.Enqueue(async ct =>
        {
            requestStarted.SetResult();
            await TestWaits.UntilCanceledAsync(ct);
            throw new InvalidOperationException("unreachable");
        });
        using var host = BuildHost(agent: agent);

        await host.StartAsync(Ct);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var exception = await Record.ExceptionAsync(() => host.StopAsync(Ct));

        Assert.Null(exception);
        var worker = host.Services.GetServices<IHostedService>().OfType<Worker>().Single();
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        var logs = host.Services.GetFakeLogCollector().GetSnapshot();
        Assert.DoesNotContain(logs, r => r.Level >= LogLevel.Warning);
        Assert.Contains(logs, r => r.Level == LogLevel.Debug && r.Message == "MTConnect synchronization stopped.");
    }

    [Theory]
    [InlineData("ApiKey", null, "DataAcquisition:Api:ApiKey")]
    [InlineData("ApiKey", "", "DataAcquisition:Api:ApiKey")]
    [InlineData("ApiKey", "short-key-31-characters-long-xx", "DataAcquisition:Api:ApiKey")]
    [InlineData("BaseUrl", "ftp://api:5100", "DataAcquisition:Api:BaseUrl")]
    [InlineData("BaseUrl", "/relative", "DataAcquisition:Api:BaseUrl")]
    [InlineData("RequestTimeout", "00:00:00", "DataAcquisition:Api:RequestTimeout")]
    public async Task StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(string setting, string? value, string expectedKey)
    {
        using var host = BuildHost(new Dictionary<string, string?> { [$"DataAcquisition:Api:{setting}"] = value });

        var exception = await Record.ExceptionAsync(() => host.StartAsync(Ct));

        var validation = Assert.IsType<OptionsValidationException>(exception);
        Assert.Contains(expectedKey, validation.Message);
        if (setting == "ApiKey" && !string.IsNullOrEmpty(value))
        {
            Assert.DoesNotContain(value, validation.Message);
        }
    }

    [Fact]
    public void Options_ApiWithKeyOnly_UsesLocalApiDefaults()
    {
        using var host = BuildHost();

        var options = host.Services.GetRequiredService<IOptions<ApiOptions>>().Value;

        Assert.Equal((new Uri("http://localhost:5100"), TimeSpan.FromSeconds(10)), (options.BaseUrl, options.RequestTimeout));
    }

    [Fact]
    public void Services_DefaultRegistration_UsesApiSinkAndHttpAgent()
    {
        using var host = BuildHost();

        Assert.IsType<ApiObservationSink>(host.Services.GetRequiredService<IObservationSink>());
        Assert.IsType<MTConnectClient>(host.Services.GetRequiredService<IMTConnectAgent>());
    }

    /// <summary>Random per test run, so no key literal lives in the repository.</summary>
    internal static readonly string TestApiKey = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    /// <summary>Same registrations as Program.cs (with a valid test API key); optional fake agent replaces HTTP.</summary>
    private static IHost BuildHost(Dictionary<string, string?>? settings = null, IMTConnectAgent? agent = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [ApiKeySetting] = TestApiKey });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        // Category filter beats the "Default" rule from appsettings, so Debug logs of the worker are collected.
        builder.Logging.ClearProviders().AddFakeLogging().AddFilter("DataAcquisition", LogLevel.Debug);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMTConnect();
        builder.Services.AddDataAcquisitionApi();
        builder.Services.AddHostedService<Worker>();
        if (agent is not null)
        {
            builder.Services.AddSingleton(agent);
        }
        return builder.Build();
    }
}
