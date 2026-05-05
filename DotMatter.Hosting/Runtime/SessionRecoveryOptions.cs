namespace DotMatter.Hosting.Runtime;

/// <summary>
/// Hosting-level options that control reconnect, startup sequencing, and background task shutdown.
/// </summary>
public sealed class SessionRecoveryOptions
{
    /// <summary>Gets or sets the duration after which a subscription is considered stale.</summary>
    public TimeSpan SubscriptionStaleThreshold { get; set; } = TimeSpan.FromSeconds(90);
    /// <summary>Gets or sets the timeout for connecting known devices during startup.</summary>
    public TimeSpan StartupConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gets or sets the timeout for endpoint discovery after connection.</summary>
    public TimeSpan EndpointDiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the timeout for reading initial device state.</summary>
    public TimeSpan StateReadTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the timeout for setting up subscriptions.</summary>
    public TimeSpan SubscriptionSetupTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the delay between host monitoring loop iterations.</summary>
    public TimeSpan MonitoringLoopDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>Gets or sets the maximum time to wait for tracked background operations during shutdown.</summary>
    public TimeSpan BackgroundShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
