using DataAcquisition.Worker.MTConnect;
using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.Api;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Registers the API client and makes it the observation sink; startup fails without a valid API key.</summary>
    public static IServiceCollection AddDataAcquisitionApi(this IServiceCollection services)
    {
        services.AddOptions<ApiOptions>()
            .BindConfiguration(ApiOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();

        services.AddHttpClient(ApiObservationSink.HttpClientName, (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<ApiOptions>>().Value;
                // Trailing slash so the relative observations path keeps a base path, e.g. http://host/da/.
                var url = options.BaseUrl.ToString();
                client.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                client.Timeout = options.RequestTimeout;
            })
            // Request logging could expose the X-Api-Key header and would duplicate the sink's own logs.
            .RemoveAllLoggers();

        services.AddSingleton<IObservationSink, ApiObservationSink>();

        return services;
    }
}
