using DotMatter.Core.Discovery;
using DotMatter.Core.Fabrics;
using DotMatter.Core.Sessions;
using Org.BouncyCastle.Math;
using System.Net;

namespace DotMatter.Core;

/// <summary>
/// Manages a resilient connection to a Matter device.
/// Handles SRP discovery, CASE session establishment, retry with exponential backoff,
/// and indefinite reconnection. Fires events for app-level hooks.
/// </summary>
public class ResilientSession(
    Fabric fabric,
    BigInteger nodeId,
    string compressedFabricId,
    string nodeOperationalId,
    IPAddress initialIp,
    ushort initialPort = 5540,
    int maxRetries = 3,
    TimeSpan? perAttemptTimeout = null,
    ResilientSessionOptions? options = null) : IAsyncDisposable, IDisposable
{
    private static readonly Random _sharedRandom = Random.Shared;
    private readonly Fabric _fabric = fabric;
    private readonly BigInteger _nodeId = nodeId;
    private readonly string _compressedFabricId = compressedFabricId;
    private readonly string _nodeOperationalId = nodeOperationalId;
    private readonly Lock _transportLock = new();
    private readonly ResilientSessionOptions _options = CreateOptions(initialPort, maxRetries, perAttemptTimeout, options);
    private IPAddress _deviceIp = initialIp;
    private ushort _devicePort = initialPort;
    private UdpConnection? _udpConn;
    private Node? _node;
    private CaseSecureSession? _session;
    private readonly int _maxRetries = options?.MaxRetries ?? maxRetries;
    private readonly TimeSpan _perAttemptTimeout = options?.PerAttemptTimeout ?? perAttemptTimeout ?? TimeSpan.FromSeconds(15);
    private DateTime _lastActivity = DateTime.UtcNow;

    /// <summary>Session idle timeout. After this, the session is considered stale and will be reconnected.</summary>
    public static TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Current CASE session (null if not connected).</summary>
    public CaseSecureSession? Session => _session;

    /// <summary>Current UDP connection (null if not connected).</summary>
    public UdpConnection? Connection => _udpConn;

    /// <summary>Current device node info.</summary>
    public Node? DeviceNode => _node;

    /// <summary>True if session and connection are active and not idle-timed-out.</summary>
    public bool IsConnected => _session != null && _udpConn != null
        && (DateTime.UtcNow - _lastActivity) < _options.SessionIdleTimeout;

    /// <summary>Last discovered IP address.</summary>
    public IPAddress DeviceIp => _deviceIp;

    /// <summary>Last discovered port.</summary>
    public ushort DevicePort => _devicePort;

    /// <summary>
    /// Fired after a CASE session is successfully established.
    /// Receives the session and UDP connection for app-level setup (state read, subscriptions).
    /// </summary>
    public event Func<CaseSecureSession, UdpConnection, Task>? Connected;

    /// <summary>
    /// Fired when the connection is torn down (before reconnect or on dispose).
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Fired when SRP discovery resolves a (possibly new) IP/port.
    /// App can persist this for faster reconnect hints.
    /// </summary>
    public event Action<IPAddress, ushort>? IpDiscovered;

    /// <summary>
    /// Establishes a CASE session with retry and exponential backoff.
    /// On success, fires <see cref="Connected"/> and <see cref="IpDiscovered"/>.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            using var activity = MatterDiagnostics.ActivitySource.StartActivity("matter.session.connect");
            activity?.SetTag("matter.connect.attempt", attempt);
            MatterDiagnostics.SessionConnectAttempts.Add(1);

            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(_perAttemptTimeout);

                CloseTransport();

                // Re-discover on retry: try SRP first (Thread), then mDNS (WiFi)
                if (attempt > 1)
                {
                    MatterLog.Info("[ResilientSession] Attempt {0}/{1}, re-discovering...", attempt, _maxRetries);
                    try
                    {
                        var (newIp, newPort) = await SrpDeviceDiscovery.DiscoverAsync(
                            _compressedFabricId,
                            _nodeOperationalId,
                            _deviceIp,
                            _devicePort,
                            new SrpDiscoveryOptions { DefaultPort = _options.DefaultPort });
                        _deviceIp = newIp;
                        _devicePort = newPort;
                    }
                    catch (MatterDiscoveryException)
                    {
                        // SRP failed (WiFi devices aren't registered in SRP) — try mDNS
                        MatterLog.Info("[ResilientSession] SRP discovery failed, trying mDNS...");
                        try
                        {
                            using var mdns = new OperationalDiscovery();
                            var cfId = ulong.Parse(_compressedFabricId, System.Globalization.NumberStyles.HexNumber);
                            var nId = ulong.Parse(_nodeOperationalId, System.Globalization.NumberStyles.HexNumber);
                            var node = await mdns.ResolveNodeAsync(cfId, nId, _options.MdnsResolveTimeout);
                            if (node != null)
                            {
                                _deviceIp = node.Address;
                                _devicePort = (ushort)node.Port;
                                MatterLog.Info("[ResilientSession] mDNS resolved: {0}:{1}", _deviceIp, _devicePort);
                            }
                        }
                        catch (Exception mdnsEx)
                        {
                            MatterLog.Warn("[ResilientSession] mDNS discovery also failed: {0}", mdnsEx.Message);
                        }
                    }
                }

                _udpConn = new UdpConnection(_deviceIp, _devicePort);
                _node = new Node
                {
                    NodeId = _nodeId,
                    Fabric = _fabric,
                    LastKnownIpAddress = _deviceIp,
                    LastKnownPort = _devicePort,
                };

                var unsecure = new UnsecureSession(_udpConn);
                var caseClient = new CASEClient(_node, _fabric, unsecure);
                _session = (CaseSecureSession?)await caseClient.EstablishSessionAsync(attemptCts.Token);
                if (_session?.Connection is UdpConnection secureConnection)
                {
                    if (!ReferenceEquals(_udpConn, secureConnection))
                    {
                        try
                        {
                            _udpConn.Close();
                        }
                        catch
                        {
                        }
                    }

                    _udpConn = secureConnection;
                }

                MatterLog.Info("[ResilientSession] CASE session established (attempt {0})", attempt);
                _lastActivity = DateTime.UtcNow;
                IpDiscovered?.Invoke(_deviceIp, _devicePort);

                if (Connected != null)
                {
                    await Connected.Invoke(_session!, _udpConn);
                }

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch (TimeoutException ex)
            {
                MatterDiagnostics.SessionConnectFailures.Add(1);
                MatterLog.Warn("[ResilientSession] Attempt {0} timed out: {1}", attempt, ex.Message);
                if (attempt < _maxRetries)
                {
                    await Task.Delay(ComputeRetryDelay(attempt, _options.MaxBackoffDelay, _options.BackoffJitterRatio), ct);
                }
            }
            catch (MatterCoreException ex)
            {
                MatterDiagnostics.SessionConnectFailures.Add(1);
                MatterLog.Warn("[ResilientSession] Attempt {0} failed: {1}", attempt, ex.Message);
                if (attempt < _maxRetries)
                {
                    await Task.Delay(ComputeRetryDelay(attempt, _options.MaxBackoffDelay, _options.BackoffJitterRatio), ct);
                }
            }
            catch (Exception ex)
            {
                MatterDiagnostics.SessionConnectFailures.Add(1);
                MatterLog.Warn("[ResilientSession] Attempt {0} failed: {1}", attempt, ex.Message);
                if (attempt < _maxRetries)
                {
                    await Task.Delay(ComputeRetryDelay(attempt, _options.MaxBackoffDelay, _options.BackoffJitterRatio), ct);
                }
            }
        }

        MatterLog.Error("[ResilientSession] All {0} attempts failed", _maxRetries);
        return false;
    }

    /// <summary>
    /// Disconnects, waits, then reconnects with retry. If initial retry fails,
    /// keeps retrying indefinitely at <paramref name="retryInterval"/> until connected or cancelled.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken ct, TimeSpan? cooldown = null, TimeSpan? retryInterval = null)
    {
        MatterDiagnostics.SessionReconnectLoops.Add(1);
        Disconnect();
        await Task.Delay(cooldown ?? _options.InitialReconnectCooldown, ct);

        if (await ConnectAsync(ct))
        {
            return;
        }

        var interval = retryInterval ?? _options.RetryInterval;
        while (!ct.IsCancellationRequested)
        {
            MatterLog.Debug("[ResilientSession] Offline, retrying in {0}s...", interval.TotalSeconds);
            await Task.Delay(interval, ct);
            if (await ConnectAsync(ct))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Tears down the current session and connection. Fires <see cref="Disconnected"/>.
    /// </summary>
    public void Disconnect()
    {
        CloseTransport();
        Disconnected?.Invoke();
    }

    /// <summary>
    /// Executes an action with the current session, auto-reconnecting on failure.
    /// </summary>
    public async Task<T?> ExecuteAsync<T>(Func<CaseSecureSession, Node, UdpConnection, Task<T>> action, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            if (!IsConnected)
            {
                if (!await ConnectAsync(ct))
                {
                    return default;
                }
            }

            // Capture refs under lock to avoid TOCTOU with Disconnect
            CaseSecureSession session;
            Node node;
            UdpConnection conn;
            lock (_transportLock)
            {
                if (_session == null || _node == null || _udpConn == null)
                {
                    continue;
                }

                session = _session;
                node = _node;
                conn = _udpConn;
            }

            try
            {
                var result = await action(session, node, conn);
                _lastActivity = DateTime.UtcNow;
                return result;
            }
            catch (Exception ex)
            {
                MatterLog.Warn("[ResilientSession] Command failed: {0}, reconnecting...", ex.Message);
                CloseTransport();
            }
        }
        return default;
    }

    /// <summary>
    /// Executes a void action with auto-reconnect.
    /// </summary>
    public async Task<bool> ExecuteAsync(Func<CaseSecureSession, Node, UdpConnection, Task> action, CancellationToken ct = default)
    {
        var result = await ExecuteAsync<bool>(async (s, n, c) =>
        {
            await action(s, n, c);
            return true;
        }, ct);
        return result;
    }

    private void CloseTransport()
    {
        CaseSecureSession? session;
        UdpConnection? conn;
        lock (_transportLock)
        {
            session = _session;
            conn = _udpConn;
            _session = null;
            _udpConn = null;
        }
        if (session != null)
        {
            try
            {
                session.Close();
            }
            catch
            {
            }
        }

        if (conn != null)
        {
            try
            {
                conn.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>DisposeAsync.</summary>
    public ValueTask DisposeAsync()
    {
        Disconnect();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    internal static TimeSpan ComputeRetryDelay(int attempt, TimeSpan maxDelay, double jitterRatio, double? jitterSample = null)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, attempt - 1), maxDelay.TotalSeconds));
        if (jitterRatio <= 0)
        {
            return baseDelay;
        }

        var sample = jitterSample ?? _sharedRandom.NextDouble();
        var factor = 1 + (((sample * 2) - 1) * jitterRatio);
        var jittered = baseDelay.TotalMilliseconds * factor;
        return TimeSpan.FromMilliseconds(Math.Max(0, jittered));
    }

    private static ResilientSessionOptions CreateOptions(
        ushort initialPort,
        int maxRetries,
        TimeSpan? perAttemptTimeout,
        ResilientSessionOptions? options)
    {
        if (options != null)
        {
            return options;
        }

        return new ResilientSessionOptions
        {
            DefaultPort = initialPort,
            MaxRetries = maxRetries,
            PerAttemptTimeout = perAttemptTimeout ?? TimeSpan.FromSeconds(15),
            SessionIdleTimeout = SessionIdleTimeout,
        };
    }
}
