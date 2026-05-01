using System.Diagnostics.Metrics;

namespace DotMatter.Controller;

/// <summary>
/// Product-level metrics for the controller host and API.
/// </summary>
public static class DotMatterProductDiagnostics
{
    private static long _commissioningAttempts;
    private static long _commissioningRejections;
    private static long _apiAuthenticationFailures;
    private static long _rateLimitRejections;
    private static long _managedReconnectRequests;
    private static long _subscriptionRestarts;
    private static long _registryPersistenceFailures;

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

    /// <summary>Counts API rate-limit rejections.</summary>
    public static readonly Counter<long> RateLimitRejections =
        Meter.CreateCounter<long>("dotmatter.api.rate_limit.rejections");

    /// <summary>Gets the commissioning attempt count since startup.</summary>
    public static long CommissioningAttemptCount => Interlocked.Read(ref _commissioningAttempts);
    /// <summary>Gets the commissioning rejection count since startup.</summary>
    public static long CommissioningRejectionCount => Interlocked.Read(ref _commissioningRejections);
    /// <summary>Gets the API authentication failure count since startup.</summary>
    public static long ApiAuthenticationFailureCount => Interlocked.Read(ref _apiAuthenticationFailures);
    /// <summary>Gets the rate-limit rejection count since startup.</summary>
    public static long RateLimitRejectionCount => Interlocked.Read(ref _rateLimitRejections);
    /// <summary>Gets the managed reconnect request count since startup.</summary>
    public static long ManagedReconnectRequestCount => Interlocked.Read(ref _managedReconnectRequests);
    /// <summary>Gets the subscription restart count since startup.</summary>
    public static long SubscriptionRestartCount => Interlocked.Read(ref _subscriptionRestarts);
    /// <summary>Gets the registry persistence failure count since startup.</summary>
    public static long RegistryPersistenceFailureCount => Interlocked.Read(ref _registryPersistenceFailures);

    /// <summary>Records one commissioning attempt.</summary>
    public static void RecordCommissioningAttempt()
    {
        CommissioningAttempts.Add(1);
        Interlocked.Increment(ref _commissioningAttempts);
    }

    /// <summary>Records one rejected commissioning attempt.</summary>
    public static void RecordCommissioningRejection()
    {
        CommissioningRejections.Add(1);
        Interlocked.Increment(ref _commissioningRejections);
    }

    /// <summary>Records one API authentication failure.</summary>
    public static void RecordApiAuthenticationFailure()
    {
        ApiAuthenticationFailures.Add(1);
        Interlocked.Increment(ref _apiAuthenticationFailures);
    }

    /// <summary>Records one API rate-limit rejection.</summary>
    public static void RecordRateLimitRejection()
    {
        RateLimitRejections.Add(1);
        Interlocked.Increment(ref _rateLimitRejections);
    }

    /// <summary>Records one managed reconnect request.</summary>
    public static void RecordManagedReconnectRequest()
    {
        ManagedReconnectRequests.Add(1);
        Interlocked.Increment(ref _managedReconnectRequests);
    }

    /// <summary>Records one subscription restart.</summary>
    public static void RecordSubscriptionRestart()
    {
        SubscriptionRestarts.Add(1);
        Interlocked.Increment(ref _subscriptionRestarts);
    }

    /// <summary>Records one registry persistence failure.</summary>
    public static void RecordRegistryPersistenceFailure()
    {
        RegistryPersistenceFailures.Add(1);
        Interlocked.Increment(ref _registryPersistenceFailures);
    }
}
