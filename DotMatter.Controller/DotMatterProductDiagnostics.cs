using System.Diagnostics.Metrics;

namespace DotMatter.Controller;

/// <summary>
/// Product-level metrics for the controller host and API.
/// </summary>
public static class DotMatterProductDiagnostics
{
    /// <summary>Name of the product metrics meter.</summary>
    public const string MeterName = "DotMatter.Product";

    /// <summary>Shared product metrics meter.</summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>Counts controller commissioning attempts.</summary>
    public static readonly Counter<long> CommissioningAttempts =
        Meter.CreateCounter<long>("dotmatter.commissioning.attempts");

    /// <summary>Counts rejected commissioning requests.</summary>
    public static readonly Counter<long> CommissioningRejections =
        Meter.CreateCounter<long>("dotmatter.commissioning.rejections");

    /// <summary>Counts failed API authentication attempts.</summary>
    public static readonly Counter<long> ApiAuthenticationFailures =
        Meter.CreateCounter<long>("dotmatter.api.auth.failures");

    /// <summary>Counts managed reconnect requests.</summary>
    public static readonly Counter<long> ManagedReconnectRequests =
        Meter.CreateCounter<long>("dotmatter.session.reconnect.requests");

    /// <summary>Counts subscription restarts.</summary>
    public static readonly Counter<long> SubscriptionRestarts =
        Meter.CreateCounter<long>("dotmatter.subscription.restarts");

    /// <summary>Counts registry persistence failures.</summary>
    public static readonly Counter<long> RegistryPersistenceFailures =
        Meter.CreateCounter<long>("dotmatter.registry.persistence.failures");
}
