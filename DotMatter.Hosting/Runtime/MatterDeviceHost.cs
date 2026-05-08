using System.Collections.Concurrent;
using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Hosting.Devices;
using DotMatter.Hosting.Storage;
using DotMatter.Hosting.Thread;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Math;

namespace DotMatter.Hosting.Runtime;

/// <summary>
/// Abstract base for Matter device host services. Provides shared infrastructure
/// for session management, state reading, subscriptions, and monitoring.
/// Consumers override virtual hooks for app-specific behavior.
/// </summary>
public abstract class MatterDeviceHost(
    ILogger logger,
    DeviceRegistry registry,
    MatterRuntimeStatus runtimeStatus,
    IOtbrService otbrService,
    SessionRecoveryOptions? recoveryOptions = null) : BackgroundService
{
    private readonly MatterRuntimeStatus _runtimeStatus = runtimeStatus;
    private readonly IOtbrService _otbrService = otbrService;
    private readonly SessionRecoveryOptions _recoveryOptions = recoveryOptions ?? new SessionRecoveryOptions();
    private readonly ConcurrentDictionary<string, Task> _ownedOperations = new();
    private DeviceStateProbe? _deviceStateProbe;
    private AttributeReportProjector? _attributeReportProjector;
    private SubscriptionCoordinator? _subscriptionCoordinator;
    private SessionLifecycleCoordinator? _sessionLifecycleCoordinator;
    /// <summary>Logger used by host infrastructure and derived services.</summary>
    protected readonly ILogger Log = logger;
    /// <summary>Registry of commissioned devices managed by the host.</summary>
    protected readonly DeviceRegistry Registry = registry;

    /// <summary>Active resilient sessions keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, ResilientSession> Sessions = new();
    /// <summary>Active subscriptions keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, Subscription> Subscriptions = new();
    /// <summary>Last successful ping time keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, DateTime> LastPing = new();
    /// <summary>Last subscription report time keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, DateTime> LastSubscriptionReport = new();
    /// <summary>Subscription stale threshold keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, TimeSpan> SubscriptionStaleThresholds = new();
    /// <summary>Tracks devices whose active subscription currently contains only event paths.</summary>
    protected readonly ConcurrentDictionary<string, bool> EventOnlySubscriptions = new();
    /// <summary>Background listener tasks keyed by device ID.</summary>
    protected readonly ConcurrentDictionary<string, Task> DeviceListeners = new();

    /// <summary>Cancellation source linked to the host lifetime.</summary>
    protected CancellationTokenSource Cts = new();

    /// <summary>Gets a value indicating whether the runtime is ready.</summary>
    public bool IsReady => _runtimeStatus.IsReady;

    private DeviceStateProbe DeviceStateProbe =>
        _deviceStateProbe ??= new DeviceStateProbe(Registry, Log, OnStateChanged);

    private AttributeReportProjector AttributeReportProjector =>
        _attributeReportProjector ??= new AttributeReportProjector(Registry, Log, OnStateChanged);

    private SubscriptionCoordinator SubscriptionCoordinator =>
        _subscriptionCoordinator ??= new SubscriptionCoordinator(
            Log,
            Registry,
            Subscriptions,
            LastSubscriptionReport,
            SubscriptionStaleThresholds,
            TrackSubscriptionShape,
            ProcessAttributeReports,
            ProcessEventReports);

    private SessionLifecycleCoordinator SessionLifecycleCoordinator =>
        _sessionLifecycleCoordinator ??= new SessionLifecycleCoordinator(
            Log,
            Registry,
            LastPing,
            LastSubscriptionReport,
            SubscriptionStaleThresholds,
            Subscriptions,
            DeviceStateProbe,
            SubscriptionCoordinator,
            _recoveryOptions,
            OnDeviceDisconnected,
            OnDeviceConnectedAsync,
            () => Cts.Token,
            ScheduleOwnedOperation);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = Cts.Token;
        _runtimeStatus.MarkStarting();

        Log.LogInformation("[MATTER] Starting...");

        try
        {
            await OnStartingAsync(token);

            Registry.LoadFromDisk();

            foreach (var id in Registry.DeviceIds.ToArray())
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var rs = await CreateResilientSessionAsync(id);
                if (rs == null)
                {
                    continue;
                }

                Sessions[id] = rs;

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(_recoveryOptions.StartupConnectTimeout);

                try
                {
                    if (await rs.ConnectAsync(connectCts.Token))
                    {
                        EnsureListenerRunning(id, rs, token);
                    }
                }
                catch (OperationCanceledException) when (connectCts.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    Log.LogWarning("[MATTER] {Id}: startup connect timed out", id);
                }
            }

            _runtimeStatus.MarkReady();
            Log.LogInformation("[MATTER] Startup complete. {Count} device(s) online.",
                Sessions.Count(kv => kv.Value.IsConnected));

            while (!token.IsCancellationRequested)
            {
                var utcNow = DateTime.UtcNow;

                foreach (var (id, rs) in Sessions.ToArray())
                {
                    if (!rs.IsConnected || rs.Connection is null)
                    {
                        continue;
                    }

                    EnsureListenerRunning(id, rs, token);
                }

                foreach (var (id, rs) in Sessions.ToArray())
                {
                    if (ShouldRecoverOfflineSession(id, rs))
                    {
                        ScheduleOfflineReconnect(id, rs);
                        continue;
                    }

                    if (ShouldRecoverDormantConnection(id, rs, utcNow))
                    {
                        await OnSessionInactiveAsync(id, rs, token);
                        continue;
                    }

                    if (!rs.IsConnected)
                    {
                        continue;
                    }

                    if (ShouldRecoverInactiveSession(id, utcNow))
                    {
                        await OnSessionInactiveAsync(id, rs, token);
                        continue;
                    }

                    if (rs.Session is not { } session)
                    {
                        continue;
                    }

                    if (ShouldRefreshSubscription(id, utcNow))
                    {
                        await OnSubscriptionStaleAsync(id, rs, session, token);
                    }
                }

                await Task.Delay(_recoveryOptions.MonitoringLoopDelay, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _runtimeStatus.MarkStopping();
        }
        catch (Exception ex)
        {
            _runtimeStatus.MarkStartupFailed(ex.Message);
            Log.LogError(ex, "[MATTER] Host startup failed");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.LogInformation("[MATTER] Shutting down...");
        _runtimeStatus.MarkStopping();

        if (!Cts.IsCancellationRequested)
        {
            await Cts.CancelAsync();
        }

        foreach (var (id, rs) in Sessions)
        {
            if (Subscriptions.TryRemove(id, out var sub))
            {
                ScheduleOwnedOperation($"dispose-subscription:{id}", async _ => await sub.DisposeAsync().AsTask());
            }

            rs.Disconnect();
            LastSubscriptionReport.TryRemove(id, out _);
            SubscriptionStaleThresholds.TryRemove(id, out _);
            EventOnlySubscriptions.TryRemove(id, out _);
            DeviceListeners.TryRemove(id, out _);
            Registry.Update(id, d => d.IsOnline = false);
            OnDeviceDisconnected(id);
        }

        Sessions.Clear();
        await DrainTrackedTasksAsync(cancellationToken);

        Log.LogInformation("[MATTER] All devices disconnected.");
        await base.StopAsync(cancellationToken);
    }

    /// <summary>Called once during startup before loading devices. Good for SRP enable, etc.</summary>
    protected virtual Task OnStartingAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Called after a CASE session is established and state is loaded.</summary>
    protected virtual Task OnDeviceConnectedAsync(string id, ISession session) => Task.CompletedTask;

    /// <summary>Called when a device disconnects.</summary>
    protected virtual void OnDeviceDisconnected(string id)
    {
    }

    /// <summary>Called when a device state changes from a subscription report.</summary>
    protected virtual void OnStateChanged(string id, string endpoint, string capability, string value)
    {
    }

    /// <summary>Called when a subscription becomes stale. Default: reconnect and recreate subscription.</summary>
    protected virtual Task OnSubscriptionStaleAsync(
        string id, ResilientSession rs, ISession session, CancellationToken ct)
    {
        Log.LogInformation("[MATTER] {Id}: subscription stale, reconnecting to refresh subscription", id);
        if (TryScheduleManagedReconnect(id, rs))
        {
            Log.LogInformation("[MATTER] {Id}: scheduling managed reconnect after subscription became stale", id);
        }

        return Task.CompletedTask;
    }

    /// <summary>Called when a device still looks connected but has gone inactive without a healthy subscription/session.</summary>
    protected virtual Task OnSessionInactiveAsync(string id, ResilientSession rs, CancellationToken ct)
    {
        Log.LogInformation("[MATTER] {Id}: device activity stale without an active subscription/session, reconnecting", id);
        if (TryScheduleManagedReconnect(id, rs))
        {
            Log.LogInformation("[MATTER] {Id}: scheduling managed reconnect after session activity became stale", id);
        }

        return Task.CompletedTask;
    }

    /// <summary>Creates a resilient session for a known device.</summary>
    public async Task<ResilientSession?> CreateResilientSessionAsync(string id)
        => await SessionLifecycleCoordinator.CreateAsync(id);

    /// <summary>Starts a per-device listener if one is not already running.</summary>
    protected void EnsureListenerRunning(string id, ResilientSession rs, CancellationToken ct)
    {
        if (DeviceListeners.TryGetValue(id, out var existing) && !existing.IsCompleted)
        {
            return;
        }

        var task = TrackDeviceListenerAsync(id, rs, ct);
        DeviceListeners[id] = task;
    }

    /// <summary>Returns a connected session owner with an active operational session.</summary>
    protected bool TryGetLiveSession(string deviceId, out ResilientSession session)
    {
        if (Sessions.TryGetValue(deviceId, out session!)
            && session.IsConnected
            && session.Session is not null)
        {
            return true;
        }

        session = null!;
        return false;
    }

    /// <summary>Ensures a device has an active operational session, reconnecting or creating one when needed.</summary>
    protected async Task<bool> EnsureDeviceSessionConnectedAsync(
        string deviceId,
        ConcurrentDictionary<string, byte> reconnecting,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reconnecting);

        if (TryGetLiveSession(deviceId, out _))
        {
            return true;
        }

        if (!reconnecting.TryAdd(deviceId, 0))
        {
            return await WaitForReconnectAsync(deviceId, reconnecting, ct);
        }

        try
        {
            var hadExistingSession = Sessions.TryGetValue(deviceId, out var session);
            if (!hadExistingSession)
            {
                session = await CreateResilientSessionAsync(deviceId);
                if (session is null)
                {
                    return false;
                }

                session = Sessions.GetOrAdd(deviceId, session);
            }

            if (session is not null && session.IsConnected && session.Session is not null)
            {
                return true;
            }

            using var reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct, Cts.Token);
            reconnectCts.CancelAfter(_recoveryOptions.StartupConnectTimeout);

            Log.LogInformation("[MATTER] {Id}: reconnecting inactive session", deviceId);

            if (session is null)
            {
                return false;
            }

            if (hadExistingSession)
            {
                await session.ReconnectAsync(
                    reconnectCts.Token,
                    cooldown: TimeSpan.Zero,
                    retryInterval: TimeSpan.FromSeconds(2));
            }
            else
            {
                await session.ConnectAsync(reconnectCts.Token);
            }

            return session.IsConnected && session.Session is not null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.LogWarning("[MATTER] {Id}: reconnect timed out after {Timeout}", deviceId, _recoveryOptions.StartupConnectTimeout);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "[MATTER] {Id}: reconnect failed", deviceId);
            return false;
        }
        finally
        {
            reconnecting.TryRemove(deviceId, out _);
        }
    }

    private async Task<bool> WaitForReconnectAsync(
        string deviceId,
        ConcurrentDictionary<string, byte> reconnecting,
        CancellationToken ct)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, Cts.Token);
        waitCts.CancelAfter(_recoveryOptions.StartupConnectTimeout);

        try
        {
            while (!waitCts.Token.IsCancellationRequested)
            {
                if (TryGetLiveSession(deviceId, out _))
                {
                    return true;
                }

                if (!reconnecting.ContainsKey(deviceId))
                {
                    break;
                }

                await Task.Delay(250, waitCts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }

        return TryGetLiveSession(deviceId, out _);
    }

    /// <summary>Tracks whether the current subscription shape for a device is event-only.</summary>
    protected void TrackSubscriptionShape(string id, int attributePathCount, int eventPathCount)
    {
        if (attributePathCount == 0 && eventPathCount > 0)
        {
            EventOnlySubscriptions[id] = true;
            return;
        }

        EventOnlySubscriptions.TryRemove(id, out _);
    }

    /// <summary>Returns whether the device subscription should be refreshed based on observed liveness.</summary>
    protected bool ShouldRefreshSubscription(string id, DateTime utcNow)
    {
        var device = Registry.Get(id);
        if (!SupportsManagedSubscriptions(device))
        {
            return false;
        }

        var staleThreshold = SubscriptionStaleThresholds.TryGetValue(id, out var threshold)
            ? threshold
            : _recoveryOptions.SubscriptionStaleThreshold;

        if (!Subscriptions.ContainsKey(id))
        {
            // Avoid launching a duplicate reconnect during the short window between CASE setup and subscription registration.
            return LastSubscriptionReport.TryGetValue(id, out var lastKnownReport)
                   && utcNow - lastKnownReport > staleThreshold;
        }

        if (!LastSubscriptionReport.TryGetValue(id, out var lastReport))
        {
            return false;
        }

        if (utcNow - lastReport <= staleThreshold)
        {
            return false;
        }

        return true;
    }

    /// <summary>Returns whether a connected device has gone inactive without valid subscription tracking.</summary>
    protected bool ShouldRecoverInactiveSession(string id, DateTime utcNow)
    {
        var device = Registry.Get(id);
        if (!SupportsManagedSubscriptions(device))
        {
            return false;
        }

        if (Subscriptions.ContainsKey(id) || LastSubscriptionReport.ContainsKey(id))
        {
            return false;
        }

        if (device?.LastSeen is not { } lastSeen)
        {
            return false;
        }

        var staleThreshold = SubscriptionStaleThresholds.TryGetValue(id, out var threshold)
            ? threshold
            : _recoveryOptions.SubscriptionStaleThreshold;

        return utcNow - lastSeen > staleThreshold;
    }

    /// <summary>Returns whether a device still marked online has lost its active session/transport and needs a reconnect.</summary>
    protected bool ShouldRecoverDormantConnection(string id, ResilientSession rs, DateTime utcNow)
    {
        var device = Registry.Get(id);
        if (!SupportsManagedSubscriptions(device) || device?.IsOnline != true)
        {
            return false;
        }

        if (rs.IsConnected && rs.Session is not null && rs.Connection is not null)
        {
            return false;
        }

        var staleThreshold = SubscriptionStaleThresholds.TryGetValue(id, out var threshold)
            ? threshold
            : _recoveryOptions.SubscriptionStaleThreshold;

        var lastActivity = device.LastSeen;
        if (LastPing.TryGetValue(id, out var lastPing)
            && (!lastActivity.HasValue || lastPing > lastActivity.Value))
        {
            lastActivity = lastPing;
        }

        if (!lastActivity.HasValue)
        {
            return false;
        }

        return utcNow - lastActivity.Value > staleThreshold;
    }

    /// <summary>Returns whether an offline commissioned device should keep trying to reconnect.</summary>
    protected bool ShouldRecoverOfflineSession(string id, ResilientSession rs)
    {
        if (rs.IsConnected && rs.Session is not null && rs.Connection is not null)
        {
            return false;
        }

        return SupportsManagedSubscriptions(Registry.Get(id));
    }

    private void ScheduleOfflineReconnect(string id, ResilientSession rs)
    {
        if (TryScheduleManagedReconnect(id, rs))
        {
            Log.LogInformation("[MATTER] {Id}: offline session, scheduling managed reconnect", id);
        }
    }

    private static bool SupportsManagedSubscriptions(DeviceInfo? device)
    {
        if (device is null)
        {
            return false;
        }

        if (device.Endpoints is not { Count: > 0 } endpoints)
        {
            return true;
        }

        foreach (var clusterId in endpoints.Values.SelectMany(static clusters => clusters).Distinct())
        {
            if (clusterId == OnOffCluster.ClusterId
                || clusterId == LevelControlCluster.ClusterId
                || clusterId == ColorControlCluster.ClusterId
                || clusterId == SwitchCluster.ClusterId
                || ClusterEventRegistry.SupportsCluster(clusterId))
            {
                return true;
            }
        }

        return false;
    }

    private async Task TrackDeviceListenerAsync(string id, ResilientSession rs, CancellationToken ct)
    {
        try
        {
            await RunDeviceListenerAsync(id, rs, ct);
        }
        finally
        {
            DeviceListeners.TryRemove(id, out _);
        }
    }

    private async Task RunDeviceListenerAsync(string id, ResilientSession rs, CancellationToken ct)
    {
        Log.LogDebug("[MATTER] {Id}: listener started", id);
        try
        {
            while (!ct.IsCancellationRequested && rs.IsConnected && rs.Connection is { } udp)
            {
                var bytes = await udp.UnroutedMessages.ReadAsync(ct);
                var processedSubscriptionReport = await TryProcessSubscriptionMessageAsync(id, bytes);
                if (processedSubscriptionReport)
                {
                    LastSubscriptionReport[id] = DateTime.UtcNow;
                }

                Registry.Update(id, d => d.LastSeen = DateTime.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, "[MATTER] {Id}: listener error", id);
        }

        Log.LogDebug("[MATTER] {Id}: listener stopped", id);
    }

    /// <summary>Attempts to process an unrouted message as a subscription report for the specified device.</summary>
    protected virtual async Task<bool> TryProcessSubscriptionMessageAsync(string id, byte[] bytes)
    {
        if (Subscriptions.TryGetValue(id, out var sub))
        {
            return await sub.ProcessIncomingBytesAsync(bytes);
        }

        return false;
    }

    /// <summary>Discovers endpoints and server clusters for a connected device.</summary>
    protected async Task DiscoverDeviceEndpointsAsync(string id, ISession session, CancellationToken ct = default)
        => await DeviceStateProbe.DiscoverDeviceEndpointsAsync(id, session, ct);

    /// <summary>Reads known state attributes for a connected device.</summary>
    protected async Task ReadDeviceStateAsync(string id, ISession session, CancellationToken ct = default)
        => await DeviceStateProbe.ReadDeviceStateAsync(id, session, ct);

    /// <summary>Starts attribute subscriptions for a connected device.</summary>
    protected async Task StartSubscriptionAsync(string id, ISession session, CancellationToken ct = default)
        => await SubscriptionCoordinator.StartAsync(id, session, ct);

    /// <summary>Processes subscription attribute reports.</summary>
    protected virtual void ProcessAttributeReports(string id, ReportDataAction report)
        => AttributeReportProjector.Process(id, report);

    /// <summary>Processes subscription event reports.</summary>
    protected virtual void ProcessEventReports(string id, IReadOnlyList<MatterEventReport> reports)
    {
    }

    /// <summary>Schedules a host-owned background operation if one with the same key is not running.</summary>
    protected bool ScheduleOwnedOperation(string key, Func<CancellationToken, Task> work)
    {
        if (_ownedOperations.TryGetValue(key, out var existing) && !existing.IsCompleted)
        {
            return false;
        }

        var task = TrackOwnedOperationAsync(key, work);
        _ownedOperations[key] = task;
        return true;
    }

    /// <summary>Returns the single operation key used for every session establishment path for a device.</summary>
    protected static string GetSessionConnectionOperationKey(string id) => $"session-connect:{id}";

    /// <summary>Schedules a managed reconnect for a device session.</summary>
    protected virtual bool TryScheduleManagedReconnect(string id, ResilientSession rs)
        => ScheduleOwnedOperation(GetSessionConnectionOperationKey(id), async ct =>
        {
            try
            {
                Log.LogInformation("[MATTER] {Id}: starting managed reconnect", id);
                await rs.ReconnectAsync(
                    ct,
                    cooldown: _recoveryOptions.ManagedReconnectInitialDelay,
                    retryInterval: _recoveryOptions.ManagedReconnectRetryInterval);
                EnsureListenerRunning(id, rs, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "[MATTER] {Id}: managed reconnect failed", id);
            }
        });

    private async Task TrackOwnedOperationAsync(string key, Func<CancellationToken, Task> work)
    {
        try
        {
            await work(Cts.Token);
        }
        catch (OperationCanceledException) when (Cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "[MATTER] Background operation {Key} failed", key);
        }
        finally
        {
            _ownedOperations.TryRemove(key, out _);
        }
    }

    /// <summary>Discovers the Thread IP address reported by OTBR.</summary>
    protected Task<string?> DiscoverThreadIpAsync(ILogger log, CancellationToken ct)
        => _otbrService.DiscoverThreadIpAsync(log, ct);

    /// <summary>Gets the OTBR service used by the host.</summary>
    protected IOtbrService OtbrService => _otbrService;

    /// <summary>Formats a node identifier as the Matter operational ID.</summary>
    public static string GetNodeOperationalId(BigInteger nodeId)
    {
        var bytes = nodeId.ToByteArrayUnsigned();
        if (bytes.Length < 8)
        {
            var padded = new byte[8];
            Array.Copy(bytes, 0, padded, 8 - bytes.Length, bytes.Length);
            bytes = padded;
        }
        Array.Reverse(bytes);
        return Convert.ToHexString(bytes);
    }

    /// <summary>Returns a filesystem-safe fabric name, using a generated fallback when needed.</summary>
    public static string SanitizeFabricName(string name)
        => MatterFabricNames.SanitizeDeviceFabricName(name);

    private async Task DrainTrackedTasksAsync(CancellationToken cancellationToken)
    {
        var tasks = _ownedOperations.Values
            .Concat(DeviceListeners.Values)
            .Where(t => !t.IsCompleted)
            .Distinct()
            .ToArray();

        if (tasks.Length == 0)
        {
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_recoveryOptions.BackgroundShutdownTimeout);

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            Log.LogWarning("[MATTER] Timed out while draining background operations during shutdown.");
        }
    }
}
