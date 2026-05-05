namespace DotMatter.Controller.Configuration;

/// <summary>
/// Controller-side attestation policy applied during commissioning.
/// </summary>
public enum CommissioningAttestationPolicy
{
    /// <summary>Skip device attestation checks.</summary>
    Disabled = 0,
    /// <summary>Require DAC -> PAI chain validation and attestation signature verification.</summary>
    RequireDacChain = 1,
    /// <summary>Attempt attestation validation but allow test-device failures with warnings.</summary>
    AllowTestDevices = 2,
}

/// <summary>
/// Controller-side regulatory location passed during commissioning.
/// </summary>
public enum CommissioningRegulatoryLocation
{
    /// <summary>Indoor location.</summary>
    Indoor = 0,
    /// <summary>Outdoor location.</summary>
    Outdoor = 1,
    /// <summary>Indoor/outdoor location.</summary>
    IndoorOutdoor = 2,
}

/// <summary>
/// Security options for the LAN-hosted controller API.
/// </summary>
public sealed class ControllerSecurityOptions
{
    /// <summary>Gets or sets a value indicating whether controller API requests require an API key.</summary>
    public bool RequireApiKey { get; set; } = true;
    /// <summary>Gets or sets the request header used to carry the API key.</summary>
    public string HeaderName { get; set; } = "X-API-Key";
    /// <summary>Gets or sets the API key accepted by the controller.</summary>
    public string? ApiKey
    {
        get; set;
    }
    /// <summary>Gets or sets the allowed CORS origins. Empty disables CORS origin allowance.</summary>
    public string[] AllowedCorsOrigins { get; set; } = [];
}

/// <summary>
/// API options for rate limiting, SSE buffering, and command execution behavior.
/// </summary>
public sealed class ControllerApiOptions
{
    /// <summary>Gets or sets the number of requests permitted per rate-limit window.</summary>
    public int RateLimitPermitLimit { get; set; } = 60;
    /// <summary>Gets or sets the rate-limit window duration.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets the maximum number of requests queued by the rate limiter.</summary>
    public int RateLimitQueueLimit { get; set; } = 5;
    /// <summary>Gets or sets the per-client server-sent events buffer capacity.</summary>
    public int SseClientBufferCapacity { get; set; } = 100;
    /// <summary>Gets or sets the timeout for controller commands sent to Matter devices.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets a value indicating whether OpenAPI and Scalar endpoints are exposed.</summary>
    public bool EnableOpenApi { get; set; } = true;
}

/// <summary>
/// Application-facing commissioning options.
/// </summary>
public sealed class CommissioningOptions
{
    /// <summary>Gets or sets the default prefix used when generating fabric names.</summary>
    public string DefaultFabricNamePrefix { get; set; } = "device";
    /// <summary>Gets or sets the controller fabric material copied for newly named device directories.</summary>
    public string SharedFabricName { get; set; } = "DotMatter";
    /// <summary>Gets or sets the timeout for connecting to a newly commissioned device.</summary>
    public TimeSpan FollowUpConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the regulatory location sent during commissioning.</summary>
    public CommissioningRegulatoryLocation RegulatoryLocation { get; set; } = CommissioningRegulatoryLocation.IndoorOutdoor;
    /// <summary>Gets or sets the two-letter country code sent during commissioning.</summary>
    public string RegulatoryCountryCode { get; set; } = "XX";
    /// <summary>Gets or sets the controller attestation policy used during commissioning.</summary>
    public CommissioningAttestationPolicy AttestationPolicy { get; set; } = CommissioningAttestationPolicy.RequireDacChain;
}

/// <summary>
/// Diagnostics endpoint configuration.
/// </summary>
public sealed class ControllerDiagnosticsOptions
{
    /// <summary>Gets or sets a value indicating whether the detailed runtime diagnostics endpoint is available.</summary>
    public bool EnableDetailedRuntimeEndpoint
    {
        get; set;
    }
}
