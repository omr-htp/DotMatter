#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace DotMatter.Core;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Options for session resiliency behavior.
/// </summary>
public sealed class ResilientSessionOptions
{
    /// <summary>Gets or sets the default Matter operational port.</summary>
    public ushort DefaultPort { get; set; } = 5540;
    /// <summary>Gets or sets the maximum number of reconnect attempts.</summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>Gets or sets the timeout for each connection attempt.</summary>
    public TimeSpan PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gets or sets the idle duration after which a session should be refreshed.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>Gets or sets the initial delay before reconnecting a failed session.</summary>
    public TimeSpan InitialReconnectCooldown { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets the interval between reconnect attempts after the initial cooldown.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets or sets the maximum randomized reconnect backoff.</summary>
    public TimeSpan MaxBackoffDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the reconnect jitter ratio applied to backoff delays.</summary>
    public double BackoffJitterRatio { get; set; } = 0.2;
    /// <summary>Gets or sets the timeout for mDNS operational address resolution.</summary>
    public TimeSpan MdnsResolveTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Options for Thread SRP and DNS discovery.
/// </summary>
public sealed class SrpDiscoveryOptions
{
    /// <summary>Gets or sets the default Matter operational port used for SRP discovery results.</summary>
    public ushort DefaultPort { get; set; } = 5540;
}

/// <summary>
/// Options for Linux BlueZ BLE commissioning transport behavior.
/// </summary>
public sealed class LinuxBleOptions
{
    /// <summary>Gets or sets the maximum duration for BLE device scanning.</summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets or sets the timeout for a single BLE connection attempt.</summary>
    public TimeSpan ConnectAttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gets or sets the timeout for resolving GATT services and characteristics.</summary>
    public TimeSpan GattResolveTimeout { get; set; } = TimeSpan.FromSeconds(20);
    /// <summary>Gets or sets the timeout for the BLE transport handshake.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Gets or sets the initial delay before sending BLE acknowledgements.</summary>
    public TimeSpan AckInitialDelay { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Gets or sets the periodic BLE acknowledgement interval.</summary>
    public TimeSpan AckPeriod { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the delay between bluetoothctl scan retries.</summary>
    public TimeSpan BluetoothctlScanRetryDelay { get; set; } = TimeSpan.FromSeconds(4);
}
