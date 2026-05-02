using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Math;
using System.Collections.Concurrent;

namespace DotMatter.Hosting;

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
            ProcessAttributeReports,
            ProcessEventReports);

    private SessionLifecycleCoordinator SessionLifecycleCoordinator =>
        _sessionLifecycleCoordinator ??= new SessionLifecycleCoordinator(
            Log,
            Registry,
            LastPing,
            LastSubscriptionReport,
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
                    await rs.ConnectAsync(connectCts.Token);
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
                    if (!rs.IsConnected || rs.Session is not { } session)
                    {
                        continue;
                    }

                    if (LastSubscriptionReport.TryGetValue(id, out var lastReport) &&
                        DateTime.UtcNow - lastReport > _recoveryOptions.SubscriptionStaleThreshold)
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
    protected virtual void OnDeviceDisconnected(string id) { }

    /// <summary>Called when a device state changes from a subscription report.</summary>
    protected virtual void OnStateChanged(string id, string endpoint, string capability, string value) { }

    /// <summary>Called when a subscription becomes stale. Default: re-subscribe.</summary>
    protected virtual async Task OnSubscriptionStaleAsync(
        string id, ResilientSession rs, ISession session, CancellationToken ct)
    {
        Log.LogInformation("[MATTER] {Id}: subscription stale, re-subscribing...", id);
        if (await TryRefreshSubscriptionAsync(id, session, ct))
        {
            return;
        }

        if (TryScheduleManagedReconnect(id, rs))
        {
            Log.LogInformation("[MATTER] {Id}: scheduling managed reconnect after subscription failure", id);
        }
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

    /// <summary>Attempts to refresh a stale subscription.</summary>
    protected virtual async Task<bool> TryRefreshSubscriptionAsync(string id, ISession session, CancellationToken ct)
    {
        try
        {
            using var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            subCts.CancelAfter(_recoveryOptions.SubscriptionSetupTimeout);
            await StartSubscriptionAsync(id, session, subCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "[MATTER] {Id}: re-subscribe failed", id);
            return false;
        }
    }

    /// <summary>Schedules a managed reconnect for a device session.</summary>
    protected virtual bool TryScheduleManagedReconnect(string id, ResilientSession rs)
        => ScheduleOwnedOperation($"reconnect:{id}", async ct =>
        {
            try
            {
                Log.LogInformation("[MATTER] {Id}: starting managed reconnect", id);
                await rs.ReconnectAsync(ct);
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
    {
        var sanitized = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')]).Trim('-', '_');
        return string.IsNullOrEmpty(sanitized) ? $"matter-{DateTime.UtcNow:yyyyMMddHHmmss}" : sanitized;
    }

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
