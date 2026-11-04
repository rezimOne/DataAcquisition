using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DataAcquisition.Api.ApiKey;

public static class ApiKeyServiceCollectionExtensions
{
    /// <summary>Registers X-Api-Key authentication; startup fails when the key is not configured.</summary>
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services.AddOptions<ApiKeyOptions>()
            .BindConfiguration(ApiKeyOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();

        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();

        return services;
    }
}
