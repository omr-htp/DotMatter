using DotMatter.Core;
using DotMatter.Hosting;

namespace DotMatter.Controller;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddControllerOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ControllerSecurityOptions>()
            .Bind(configuration.GetSection("Controller:Security"))
            .Validate(o => !o.RequireApiKey || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Controller security requires a non-empty API key when RequireApiKey is enabled.")
            .ValidateOnStart();

        services.AddOptions<ControllerApiOptions>()
            .Bind(configuration.GetSection("Controller:Api"))
            .ValidateOnStart();

        services.AddOptions<CommissioningOptions>()
            .Bind(configuration.GetSection("Controller:Commissioning"))
            .ValidateOnStart();

        services.AddOptions<RegistryOptions>()
            .Bind(configuration.GetSection("Controller:Registry"))
            .ValidateOnStart();

        services.AddOptions<SessionRecoveryOptions>()
            .Bind(configuration.GetSection("Controller:SessionRecovery"))
            .ValidateOnStart();

        services.AddOptions<OtbrServiceOptions>()
            .Bind(configuration.GetSection("Controller:Otbr"))
            .ValidateOnStart();

        services.AddOptions<MatterLogSettings>()
            .Bind(configuration.GetSection("Controller:MatterLog"));

        return services;
    }
}
