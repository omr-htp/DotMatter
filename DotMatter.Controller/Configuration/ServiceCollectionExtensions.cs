using DotMatter.Core;
using DotMatter.Hosting.Devices;
using DotMatter.Hosting.Runtime;
using DotMatter.Hosting.Thread;

namespace DotMatter.Controller.Configuration;

internal static class ServiceCollectionExtensions
{
    internal static ControllerSecurityOptions NormalizeSecurityOptions(ControllerSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.HeaderName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "X-API-Key"
            : options.HeaderName.Trim();

        options.ApiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? null
            : options.ApiKey.Trim();

        return options;
    }

    internal static IServiceCollection AddControllerOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        static bool IsTwoLetterCountryCode(string value)
            => value.Length == 2 && value.All(static c => c is >= 'A' and <= 'Z');

        services.AddOptions<ControllerSecurityOptions>()
            .Bind(configuration.GetSection("Controller:Security"))
            .PostConfigure(static options => NormalizeSecurityOptions(options))
            .Validate(o => !o.RequireApiKey || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Controller security requires a non-empty API key when RequireApiKey is enabled.")
            .ValidateOnStart();

        services.AddOptions<ControllerApiOptions>()
            .Bind(configuration.GetSection("Controller:Api"))
            .ValidateOnStart();

        services.AddOptions<CommissioningOptions>()
            .Bind(configuration.GetSection("Controller:Commissioning"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFabricNamePrefix),
                "Controller commissioning requires a non-empty default fabric name prefix.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SharedFabricName),
                "Controller commissioning requires a non-empty shared fabric name.")
            .Validate(o => IsTwoLetterCountryCode(o.RegulatoryCountryCode),
                "Controller commissioning requires a two-letter uppercase regulatory country code.")
            .Validate(o => Enum.IsDefined(o.RegulatoryLocation),
                "Controller commissioning regulatory location must be a supported value.")
            .Validate(o => Enum.IsDefined(o.AttestationPolicy),
                "Controller commissioning attestation policy must be a supported value.")
            .ValidateOnStart();

        services.AddOptions<ControllerDiagnosticsOptions>()
            .Bind(configuration.GetSection("Controller:Diagnostics"))
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
