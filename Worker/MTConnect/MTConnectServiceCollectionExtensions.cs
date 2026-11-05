using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.MTConnect;

public static class MTConnectServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MTConnect agent client and synchronizer. The observation sink is registered separately
    /// (AddDataAcquisitionApi in the worker; a test double in tests).
    /// </summary>
    public static IServiceCollection AddMTConnect(this IServiceCollection services)
    {
        services.AddOptions<MTConnectOptions>()
            .BindConfiguration(MTConnectOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MTConnectOptions>, MTConnectOptionsValidator>();

        services.AddHttpClient(MTConnectClient.HttpClientName, (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MTConnectOptions>>().Value;
                // Trailing slash so relative URLs keep a path prefix, e.g. http://host/agent/ + probe.
                var url = options.AgentUrl.ToString();
                client.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                client.Timeout = options.RequestTimeout;
            })
            // Request logs every PollInterval would drown the synchronizer's own logs.
            .RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMTConnectAgent, MTConnectClient>();
        services.AddSingleton<MTConnectSynchronizer>();

        return services;
    }
}
