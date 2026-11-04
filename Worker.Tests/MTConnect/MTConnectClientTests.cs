using System.Net;
using DataAcquisition.Worker.MTConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataAcquisition.Worker.Tests.MTConnect;

public sealed class MTConnectClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProbeAsync_DefaultAgentUrl_RequestsProbeAndParsesDevices()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Fixtures.Read("probe.xml"));
        var agent = CreateAgent(handler);

        var devices = await agent.ProbeAsync(Ct);

        Assert.Equal("http://localhost:5000/probe", handler.RequestedUrls.Single());
        Assert.Equal(["VMC5AX"], devices.DeviceNames);
    }

    [Fact]
    public async Task SampleAsync_AgentUrlWithPath_KeepsPathAndSendsFromAndCount()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Fixtures.Read("sample.xml"));
        var agent = CreateAgent(handler, new() { ["DataAcquisition:MTConnect:AgentUrl"] = "http://agent.local:8080/mtc" });

        var sample = await agent.SampleAsync(41, 1000, Ct);

        Assert.Equal("http://agent.local:8080/mtc/sample?from=41&count=1000", handler.RequestedUrls.Single());
        Assert.Equal(40, sample.Observations.Count);
    }

    [Fact]
    public async Task CurrentAsync_Ok_RequestsCurrent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Fixtures.Read("current.xml"));

        await CreateAgent(handler).CurrentAsync(Ct);

        Assert.Equal("http://localhost:5000/current", handler.RequestedUrls.Single());
    }

    [Fact]
    public async Task SampleAsync_OutOfRange404_ThrowsAgentErrorWithCode()
    {
        var agent = CreateAgent(new StubHandler(HttpStatusCode.NotFound, Fixtures.Read("error-out-of-range.xml")));

        var error = await Assert.ThrowsAsync<MTConnectAgentErrorException>(() => agent.SampleAsync(999999, 10, Ct));

        Assert.Equal(MTConnectAgentErrorException.OutOfRange, error.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.NotFound, "<html>not found</html>")]
    public async Task SampleAsync_ErrorStatusWithoutMTConnectError_ThrowsHttpRequestException(HttpStatusCode status, string body)
    {
        var agent = CreateAgent(new StubHandler(status, body));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => agent.SampleAsync(1, 10, Ct));

        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task CurrentAsync_OkWithMalformedXml_ThrowsFormatException()
    {
        var agent = CreateAgent(new StubHandler(HttpStatusCode.OK, Fixtures.Read("malformed.xml")));

        await Assert.ThrowsAsync<MTConnectFormatException>(() => agent.CurrentAsync(Ct));
    }

    [Fact]
    public async Task CurrentAsync_AgentDoesNotAnswer_TimesOutAfterRequestTimeout()
    {
        var agent = CreateAgent(StubHandler.Hanging(), new() { ["DataAcquisition:MTConnect:RequestTimeout"] = "00:00:01" });

        var error = await Record.ExceptionAsync(() => agent.CurrentAsync(Ct));

        var canceled = Assert.IsAssignableFrom<TaskCanceledException>(error);
        Assert.IsType<TimeoutException>(canceled.InnerException);
    }

    private static IMTConnectAgent CreateAgent(HttpMessageHandler handler, Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddLogging();
        services.AddMTConnect();
        services.AddHttpClient(MTConnectClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IMTConnectAgent>();
    }

    private sealed class StubHandler(HttpStatusCode status, string body, bool hang = false) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        public static StubHandler Hanging() => new(HttpStatusCode.OK, "", hang: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
