using DotMatter.Core;
using DotMatter.Core.Clusters;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Hosting;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Math;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using ISession = DotMatter.Core.Sessions.ISession;

namespace DotMatter.Controller;

/// <summary>
/// Commands supported by the On/Off device API.
/// </summary>
public enum DeviceCommand
{
    /// <summary>Turn on.</summary>
    On,
    /// <summary>Turn off.</summary>
    Off,
    /// <summary>Toggle on/off state.</summary>
    Toggle,
}

/// <summary>
/// Represents an active server-sent events subscription.
/// </summary>
public sealed class ControllerEventSubscription(ChannelReader<string> reader, Func<ValueTask> disposeAsync) : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync = disposeAsync;

    /// <summary>Gets the stream reader for serialized event payloads.</summary>
    public ChannelReader<string> Reader { get; } = reader;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _disposeAsync();
}

/// <summary>
/// Controller-specific Matter device host. Extends MatterDeviceHost with
/// REST API command methods and SSE event broadcasting.
/// </summary>
public sealed class MatterControllerService(
    ILogger<MatterControllerService> log,
    DeviceRegistry registry,
    MatterRuntimeStatus runtimeStatus,
    IOtbrService otbrService,
    IOptions<SessionRecoveryOptions> recoveryOptions,
    IOptions<ControllerApiOptions> apiOptions,
    IOptions<CommissioningOptions> commissioningOptions) : MatterDeviceHost(log, registry, runtimeStatus, otbrService, recoveryOptions.Value)
{
    private readonly ConcurrentDictionary<int, Channel<string>> _sseClients = new();
    private readonly ConcurrentDictionary<int, Channel<string>> _matterEventClients = new();
    private readonly ControllerApiOptions _apiOptions = apiOptions.Value;
    private readonly CommissioningOptions _commissioningOptions = commissioningOptions.Value;
    private readonly DeviceCommandExecutor _commandExecutor = new(log, registry, apiOptions);
    private int _nextClientId;
    private int _nextMatterEventClientId;

    internal sealed record DeviceBindingQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceBindingListResponse? Response,
        string? Error = null);

    internal sealed record DeviceAclQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceAclListResponse? Response,
        string? Error = null);

    internal sealed record DeviceBindingRemovalResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceBindingRemovalResponse? Response,
        string? Error = null);

    internal sealed record DeviceAclRemovalResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceAclRemovalResponse? Response,
        string? Error = null);

    internal sealed record SwitchBindingRemovalResult(
        bool Success,
        DeviceOperationFailure Failure,
        SwitchBindingRemovalResponse? Response,
        string? Error = null);

    internal sealed record FabricBindingQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        FabricBindingListResponse? Response,
        string? Error = null);

    internal sealed record FabricAclQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        FabricAclListResponse? Response,
        string? Error = null);

    internal sealed record DeviceMatterEventQueryResult(
        bool Success,
        DeviceOperationFailure Failure,
        DeviceMatterEventReadResponse? Response,
        string? Error = null);

    private sealed record BindingRemovalPlan(
        BindingCluster.TargetStruct[] UpdatedEntries,
        BindingCluster.TargetStruct[] RemovedEntries,
        RemovalStatus Status)
    {
        public bool RequiresWrite => RemovedEntries.Length > 0;
    }

    private sealed record AclRemovalPlan(
        AccessControlCluster.AccessControlEntryStruct[] UpdatedEntries,
        AccessControlCluster.AccessControlEntryStruct[] RemovedEntries,
        RemovalStatus Status)
    {
        public bool RequiresWrite => RemovedEntries.Length > 0;
    }

    /// <summary>Creates a per-client SSE channel.</summary>
    public ControllerEventSubscription SubscribeEvents(CancellationToken ct)
        => SubscribeClient(_sseClients, ref _nextClientId, RemoveSseClient, ct);

    /// <summary>Creates a per-client SSE channel for live Matter event envelopes.</summary>
    public ControllerEventSubscription SubscribeMatterEvents(CancellationToken ct)
        => SubscribeClient(_matterEventClients, ref _nextMatterEventClientId, RemoveMatterEventClient, ct);

    private ControllerEventSubscription SubscribeClient(
        ConcurrentDictionary<int, Channel<string>> clients,
        ref int nextClientId,
        Action<int> removeClient,
        CancellationToken ct)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(_apiOptions.SseClientBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var clientId = Interlocked.Increment(ref nextClientId);
        clients[clientId] = channel;
        var registration = ct.Register(() => removeClient(clientId));

        return new ControllerEventSubscription(
            channel.Reader,
            () =>
            {
                registration.Dispose();
                removeClient(clientId);
                return ValueTask.CompletedTask;
            });
    }

    private void RemoveSseClient(int clientId)
    {
        if (_sseClients.TryRemove(clientId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private void RemoveMatterEventClient(int clientId)
    {
        if (_matterEventClients.TryRemove(clientId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private void BroadcastEvent(string evt)
        => BroadcastSerializedEvent(_sseClients, evt);

    private void BroadcastMatterEvent(string evt)
        => BroadcastSerializedEvent(_matterEventClients, evt);

    private static void BroadcastSerializedEvent(ConcurrentDictionary<int, Channel<string>> clients, string evt)
    {
        if (clients.IsEmpty)
        {
            return;
        }

        foreach (var (_, ch) in clients)
        {
            ch.Writer.TryWrite(evt);
        }
    }

    private void PublishEvent(string id, string type, string value)
    {
        var json = JsonSerializer.Serialize(
            new DeviceEvent(id, type, value, DateTime.UtcNow),
            ControllerJsonContext.Default.DeviceEvent);
        BroadcastEvent(json);
    }

    private void PublishMatterEvent(MatterEventResponse evt)
    {
        var json = JsonSerializer.Serialize(evt, ControllerJsonContext.Default.MatterEventResponse);
        BroadcastMatterEvent(json);
    }

    /// <inheritdoc />
    protected override async Task OnStartingAsync(CancellationToken ct)
    {
        try
        {
            await OtbrService.EnableSrpServerAsync(ct);
            Log.LogInformation("SRP server enabled");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to enable SRP server.");
        }
    }

    /// <inheritdoc />
    protected override Task OnDeviceConnectedAsync(string id, ISession session)
    {
        PublishEvent(id, "state", "online");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void OnDeviceDisconnected(string id)
        => PublishEvent(id, "state", "offline");

    /// <inheritdoc />
    protected override async Task OnSubscriptionStaleAsync(string id, ResilientSession rs, ISession session, CancellationToken ct)
    {
        DotMatterProductDiagnostics.RecordSubscriptionRestart();
        await base.OnSubscriptionStaleAsync(id, rs, session, ct);
    }

    /// <inheritdoc />
    protected override void OnStateChanged(string id, string endpoint, string capability, string value)
    {
        string sseValue = capability switch
        {
            "on_off" => value == "true" ? "on" : "off",
            _ => $"{capability}:{value}",
        };
        PublishEvent(id, "state", sseValue);
    }

    /// <inheritdoc />
    protected override void ProcessEventReports(string id, IReadOnlyList<MatterEventReport> reports)
    {
        if (_matterEventClients.IsEmpty)
        {
            return;
        }

        var device = Registry.Get(id);
        foreach (var report in reports)
        {
            PublishMatterEvent(MapMatterEventResponse(id, device?.Name, report));
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken ct)
    {
        foreach (var clientId in _sseClients.Keys.ToArray())
        {
            RemoveSseClient(clientId);
        }

        foreach (var clientId in _matterEventClients.Keys.ToArray())
        {
            RemoveMatterEventClient(clientId);
        }

        await base.StopAsync(ct);
        Log.LogInformation("Matter Controller stopped.");
    }

    /// <summary>Returns whether the device is known to the controller.</summary>
    public bool HasDevice(string id) => Sessions.ContainsKey(id) || Registry.Get(id) != null;

    /// <summary>Connect to a newly commissioned device using an owned background operation.</summary>
    public bool TryScheduleConnectNewDevice(string deviceId)
        => ScheduleOwnedOperation($"connect-device:{deviceId}", async ct =>
        {
            try
            {
                Registry.LoadFromDisk();
                if (Log.IsEnabled(LogLevel.Information))
                {
                    Log.LogInformation("Connecting to newly commissioned device {Id}...", deviceId);
                }

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_commissioningOptions.FollowUpConnectTimeout);

                var rs = await CreateResilientSessionAsync(deviceId);
                if (rs != null)
                {
                    Sessions[deviceId] = rs;
                    if (await rs.ConnectAsync(connectCts.Token))
                    {
                        EnsureListenerRunning(deviceId, rs, ct);
                        PublishEvent(deviceId, "commissioned", "online");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Failed to connect newly commissioned device {Id}.", deviceId);
            }
        });

    /// <summary>Disconnects a device and removes its registry and fabric data.</summary>
    public bool DisconnectAndRemove(string id)
    {
        if (Sessions.TryRemove(id, out var rs))
        {
            rs.Disconnect();
        }

        Subscriptions.TryRemove(id, out _);
        LastSubscriptionReport.TryRemove(id, out _);
        LastPing.TryRemove(id, out _);
        DeviceListeners.TryRemove(id, out _);
        return Registry.Remove(id);
    }

    /// <summary>Sends an On/Off command to a connected device.</summary>
    public async Task<DeviceOperationResult> SendCommandAsync(string id, DeviceCommand cmd)
    {
        if (!TryGetConnectedSession(id, out var rs))
        {
            Log.LogWarning("Device {Id} not connected", id);
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var onOff = new OnOffCluster(rs.Session!, endpointId: GetEndpoint(id, OnOffCluster.ClusterId));
        return await _commandExecutor.ExecuteAsync(
            id,
            $"command {cmd}",
            async ct => cmd switch
            {
                DeviceCommand.On => await onOff.OnAsync(ct),
                DeviceCommand.Off => await onOff.OffAsync(ct),
                _ => await onOff.ToggleAsync(ct)
            },
            device => device.LastSeen = DateTime.UtcNow,
            cmd.ToString(),
            value => PublishEvent(id, "command", value),
            $"Device {id}: {cmd} OK",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets the level attribute for a connected device.</summary>
    public async Task<DeviceOperationResult> SetLevelAsync(string id, byte level, ushort transitionTime = 5)
    {
        if (!TryGetConnectedSession(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var cluster = new LevelControlCluster(rs.Session!, endpointId: GetEndpoint(id, LevelControlCluster.ClusterId));
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetLevel",
            ct => cluster.MoveToLevelWithOnOffAsync(
                level,
                transitionTime,
                LevelControlCluster.OptionsBitmap.ExecuteIfOff,
                LevelControlCluster.OptionsBitmap.ExecuteIfOff,
                ct),
            device =>
            {
                device.Level = level;
                device.LastSeen = DateTime.UtcNow;
            },
            $"level:{level}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Level -> {level}",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets hue and saturation for a connected device, converting to XY when needed.</summary>
    public async Task<DeviceOperationResult> SetColorAsync(string id, byte hue, byte saturation, ushort transitionTime = 5)
    {
        if (!TryGetConnectedSession(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var cluster = new ColorControlCluster(rs.Session!, endpointId: GetEndpoint(id, ColorControlCluster.ClusterId));
        var device = Registry.Get(id);
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetColor",
            async ct =>
            {
                if (device?.SupportsHueSaturation ?? true)
                {
                    return await cluster.MoveToHueAndSaturationAsync(
                        hue,
                        saturation,
                        transitionTime,
                        ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                        ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                        ct);
                }

                var (x, y) = ColorConversion.HueSatToXY(hue, saturation);
                return await cluster.MoveToColorAsync(
                    x,
                    y,
                    transitionTime,
                    ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                    ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                    ct);
            },
            currentDevice =>
            {
                currentDevice.Hue = hue;
                currentDevice.Saturation = saturation;
                currentDevice.LastSeen = DateTime.UtcNow;
            },
            $"color:h{hue}s{saturation}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Color -> H:{hue} S:{saturation}",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets the CIE XY color attributes for a connected device.</summary>
    public async Task<DeviceOperationResult> SetColorXYAsync(string id, ushort x, ushort y, ushort transitionTime = 5)
    {
        if (!TryGetConnectedSession(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var cluster = new ColorControlCluster(rs.Session!, endpointId: GetEndpoint(id, ColorControlCluster.ClusterId));
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetColorXY",
            ct => cluster.MoveToColorAsync(
                x,
                y,
                transitionTime,
                ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                ct),
            device =>
            {
                device.ColorX = x;
                device.ColorY = y;
                device.LastSeen = DateTime.UtcNow;
            },
            $"color-xy:x{x}y{y}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Color XY -> ({x}, {y})",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Configures a switch Binding entry and grants the switch OnOff operate ACL on the target.</summary>
    public async Task<DeviceOperationResult> BindSwitchOnOffAsync(
        string switchId,
        string targetId,
        ushort sourceEndpoint = 1,
        ushort targetEndpoint = 1)
    {
        if (!TryGetConnectedSession(switchId, out var switchSession))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, $"Switch {switchId} is not connected");
        }

        if (!TryGetConnectedSession(targetId, out var targetSession))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, $"Target {targetId} is not connected");
        }

        var switchDevice = Registry.Get(switchId);
        var targetDevice = Registry.Get(targetId);
        if (switchDevice is null || targetDevice is null)
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotFound, "Switch or target device was not found");
        }

        var switchNodeId = ToOperationalNodeId(switchDevice.NodeId);
        var targetNodeId = ToOperationalNodeId(targetDevice.NodeId);
        var targetFabric = await new FabricDiskStorage(Registry.BasePath).LoadFabricAsync(targetDevice.FabricName);
        var controllerNodeId = BitConverter.ToUInt64(targetFabric.RootNodeId.ToByteArrayUnsigned());

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var aclResult = await EnsureOnOffOperateAclAsync(
                targetSession.Session!,
                controllerNodeId,
                switchNodeId,
                targetEndpoint,
                cts.Token);
            if (!aclResult.Success)
            {
                return new DeviceOperationResult(false, DeviceOperationFailure.TransportError, $"ACL write failed: {FormatWriteResponse(aclResult)}");
            }

            var bindingResult = await EnsureOnOffBindingAsync(switchSession.Session!, targetNodeId, sourceEndpoint, targetEndpoint, cts.Token);
            if (!bindingResult.Success)
            {
                return new DeviceOperationResult(false, DeviceOperationFailure.TransportError, $"Binding write failed: {FormatWriteResponse(bindingResult)}");
            }

            Registry.Update(switchId, device => device.LastSeen = DateTime.UtcNow);
            Registry.Update(targetId, device => device.LastSeen = DateTime.UtcNow);
            PublishEvent(switchId, "binding", $"onoff:{targetId}");
            Log.LogInformation("Switch {SwitchId}: bound endpoint {SourceEndpoint} OnOff to {TargetId} endpoint {TargetEndpoint}",
                switchId, sourceEndpoint, targetId, targetEndpoint);
            return DeviceOperationResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.Timeout, "Switch binding timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Switch {SwitchId}: binding to target {TargetId} failed", switchId, targetId);
            return new DeviceOperationResult(false, DeviceOperationFailure.TransportError, ex.Message);
        }
    }

    /// <summary>Removes a switch Binding entry and conservatively reconciles the matching target ACL grant.</summary>
    internal async Task<SwitchBindingRemovalResult> UnbindSwitchOnOffAsync(
        string switchId,
        string targetId,
        ushort sourceEndpoint = 1,
        ushort targetEndpoint = 1)
    {
        if (!TryGetConnectedSession(switchId, out var switchSession))
        {
            return new SwitchBindingRemovalResult(false, DeviceOperationFailure.NotConnected, null, $"Switch {switchId} is not connected");
        }

        if (!TryGetConnectedSession(targetId, out var targetSession))
        {
            return new SwitchBindingRemovalResult(false, DeviceOperationFailure.NotConnected, null, $"Target {targetId} is not connected");
        }

        var switchDevice = Registry.Get(switchId);
        var targetDevice = Registry.Get(targetId);
        if (switchDevice is null || targetDevice is null)
        {
            return new SwitchBindingRemovalResult(false, DeviceOperationFailure.NotFound, null, "Switch or target device was not found");
        }

        var switchNodeId = ToOperationalNodeId(switchDevice.NodeId);
        var targetNodeId = ToOperationalNodeId(targetDevice.NodeId);

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);

            var bindingCluster = new BindingCluster(switchSession.Session!, sourceEndpoint);
            var existingBinding = await bindingCluster.ReadBindingAsync(cts.Token) ?? [];
            var bindingPlan = RemoveBindingEntries(existingBinding, entry =>
                entry.Node == targetNodeId
                && entry.Endpoint == targetEndpoint
                && entry.Cluster == OnOffCluster.ClusterId);

            if (bindingPlan.RequiresWrite)
            {
                var bindingWrite = await bindingCluster.WriteBindingAsync(bindingPlan.UpdatedEntries, ct: cts.Token);
                if (!bindingWrite.Success)
                {
                    var error = $"Binding write failed: {FormatWriteResponse(bindingWrite)}";
                    return new SwitchBindingRemovalResult(
                        false,
                        DeviceOperationFailure.TransportError,
                        new SwitchBindingRemovalResponse(
                            switchId,
                            switchDevice.Name,
                            targetId,
                            targetDevice.Name,
                            sourceEndpoint,
                            targetEndpoint,
                            CreateWriteFailedStatus(existingBinding.Length, FormatWriteResponse(bindingWrite)),
                            CreateNotAttemptedStatus(0, "ACL cleanup was not attempted because Binding removal failed"),
                            error),
                        error);
                }
            }

            var accessControl = new AccessControlCluster(targetSession.Session!, endpointId: 0);
            var existingAcl = await accessControl.ReadACLAsync(cts.Token) ?? [];
            var aclPlan = RemoveOnOffAclEntries(existingAcl, switchNodeId, targetEndpoint);

            if (aclPlan.RequiresWrite)
            {
                var aclWrite = await accessControl.WriteACLAsync(aclPlan.UpdatedEntries, ct: cts.Token);
                if (!aclWrite.Success)
                {
                    var error = $"ACL write failed: {FormatWriteResponse(aclWrite)}";
                    return new SwitchBindingRemovalResult(
                        false,
                        DeviceOperationFailure.TransportError,
                        new SwitchBindingRemovalResponse(
                            switchId,
                            switchDevice.Name,
                            targetId,
                            targetDevice.Name,
                            sourceEndpoint,
                            targetEndpoint,
                            bindingPlan.Status,
                            CreateWriteFailedStatus(existingAcl.Length, FormatWriteResponse(aclWrite)),
                            error),
                        error);
                }
            }

            Registry.Update(switchId, device => device.LastSeen = DateTime.UtcNow);
            Registry.Update(targetId, device => device.LastSeen = DateTime.UtcNow);

            if (bindingPlan.Status.RemovedCount > 0 || aclPlan.Status.RemovedCount > 0)
            {
                PublishEvent(switchId, "binding", $"onoff-removed:{targetId}");
            }

            return new SwitchBindingRemovalResult(
                true,
                DeviceOperationFailure.None,
                new SwitchBindingRemovalResponse(
                    switchId,
                    switchDevice.Name,
                    targetId,
                    targetDevice.Name,
                    sourceEndpoint,
                    targetEndpoint,
                    bindingPlan.Status,
                    aclPlan.Status));
        }
        catch (OperationCanceledException)
        {
            return new SwitchBindingRemovalResult(false, DeviceOperationFailure.Timeout, null, "Switch unbind timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Switch {SwitchId}: unbinding from target {TargetId} failed", switchId, targetId);
            return new SwitchBindingRemovalResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Removes matching Binding entries from one source device endpoint.</summary>
    internal async Task<DeviceBindingRemovalResult> RemoveBindingEntriesAsync(string id, DeviceBindingRemovalRequest request)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new DeviceBindingRemovalResult(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        if (!TryGetConnectedSession(id, out var session))
        {
            return new DeviceBindingRemovalResult(false, DeviceOperationFailure.NotConnected, null, $"Device {id} is not connected");
        }

        if (!TryParseBindingNodeId(request.NodeId, out var nodeId, out var parseError))
        {
            return new DeviceBindingRemovalResult(false, DeviceOperationFailure.TransportError, null, parseError);
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var binding = new BindingCluster(session.Session!, request.Endpoint);
            var existingBinding = await binding.ReadBindingAsync(cts.Token) ?? [];
            var targetIndex = BuildOperationalNodeIndex();
            var plan = RemoveBindingEntries(existingBinding, entry => MatchesBindingRemovalRequest(entry, nodeId, request));

            if (plan.RequiresWrite)
            {
                var write = await binding.WriteBindingAsync(plan.UpdatedEntries, ct: cts.Token);
                if (!write.Success)
                {
                    var error = $"Binding write failed: {FormatWriteResponse(write)}";
                    return new DeviceBindingRemovalResult(
                        false,
                        DeviceOperationFailure.TransportError,
                        new DeviceBindingRemovalResponse(
                            device.Id,
                            device.Name,
                            device.FabricName,
                            request.Endpoint,
                            CreateWriteFailedStatus(existingBinding.Length, FormatWriteResponse(write)),
                            [],
                            error),
                        error);
                }
            }

            Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
            if (plan.Status.RemovedCount > 0)
            {
                PublishEvent(id, "binding", $"removed:{plan.Status.RemovedCount}");
            }

            return new DeviceBindingRemovalResult(
                true,
                DeviceOperationFailure.None,
                new DeviceBindingRemovalResponse(
                    device.Id,
                    device.Name,
                    device.FabricName,
                    request.Endpoint,
                    plan.Status,
                    plan.RemovedEntries.Select(entry => MapBindingEntry(entry, targetIndex)).ToArray()));
        }
        catch (OperationCanceledException)
        {
            return new DeviceBindingRemovalResult(false, DeviceOperationFailure.Timeout, null, "Binding removal timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: Binding removal failed on endpoint {Endpoint}", id, request.Endpoint);
            return new DeviceBindingRemovalResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Removes matching ACL entries from endpoint 0 of one target device.</summary>
    internal async Task<DeviceAclRemovalResult> RemoveAclEntriesAsync(string id, DeviceAclRemovalRequest request)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new DeviceAclRemovalResult(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        if (!TryGetConnectedSession(id, out var session))
        {
            return new DeviceAclRemovalResult(false, DeviceOperationFailure.NotConnected, null, $"Device {id} is not connected");
        }

        try
        {
            var privilege = ParseAclPrivilege(request.Privilege);
            var authMode = ParseAclAuthMode(request.AuthMode);
            var auxiliaryType = ParseAclAuxiliaryType(request.AuxiliaryType);
            var subjects = ParseAclSubjects(request.Subjects);
            var targets = ParseAclTargets(request.Targets);
            var targetIndex = BuildOperationalNodeIndex();

            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var accessControl = new AccessControlCluster(session.Session!, endpointId: 0);
            var existingAcl = await accessControl.ReadACLAsync(cts.Token) ?? [];
            var plan = RemoveAclEntries(existingAcl, entry => MatchesAclRemovalRequest(entry, privilege, authMode, subjects, targets, auxiliaryType));

            if (plan.RequiresWrite)
            {
                var write = await accessControl.WriteACLAsync(plan.UpdatedEntries, ct: cts.Token);
                if (!write.Success)
                {
                    var error = $"ACL write failed: {FormatWriteResponse(write)}";
                    return new DeviceAclRemovalResult(
                        false,
                        DeviceOperationFailure.TransportError,
                        new DeviceAclRemovalResponse(
                            device.Id,
                            device.Name,
                            device.FabricName,
                            0,
                            CreateWriteFailedStatus(existingAcl.Length, FormatWriteResponse(write)),
                            [],
                            error),
                        error);
                }
            }

            Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
            if (plan.Status.RemovedCount > 0)
            {
                PublishEvent(id, "acl", $"removed:{plan.Status.RemovedCount}");
            }

            return new DeviceAclRemovalResult(
                true,
                DeviceOperationFailure.None,
                new DeviceAclRemovalResponse(
                    device.Id,
                    device.Name,
                    device.FabricName,
                    0,
                    plan.Status,
                    plan.RemovedEntries.Select(entry => MapAclEntry(entry, targetIndex)).ToArray()));
        }
        catch (OperationCanceledException)
        {
            return new DeviceAclRemovalResult(false, DeviceOperationFailure.Timeout, null, "ACL removal timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: ACL removal failed", id);
            return new DeviceAclRemovalResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Reads Binding entries from one source device endpoint.</summary>
    internal async Task<DeviceBindingQueryResult> ReadBindingsAsync(string id, ushort endpoint = 1)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new DeviceBindingQueryResult(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        if (!TryGetConnectedSession(id, out var session))
        {
            return new DeviceBindingQueryResult(false, DeviceOperationFailure.NotConnected, null, $"Device {id} is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var response = await ReadBindingsForEndpointAsync(
                device,
                session.Session!,
                endpoint,
                BuildOperationalNodeIndex(),
                cts.Token);

            return new DeviceBindingQueryResult(true, DeviceOperationFailure.None, response);
        }
        catch (OperationCanceledException)
        {
            return new DeviceBindingQueryResult(false, DeviceOperationFailure.Timeout, null, "Binding read timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: Binding read failed on endpoint {Endpoint}", id, endpoint);
            return new DeviceBindingQueryResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Reads AccessControl ACL entries from one target device.</summary>
    internal async Task<DeviceAclQueryResult> ReadAclAsync(string id)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new DeviceAclQueryResult(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        if (!TryGetConnectedSession(id, out var session))
        {
            return new DeviceAclQueryResult(false, DeviceOperationFailure.NotConnected, null, $"Device {id} is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var response = await ReadAclForEndpointAsync(
                device,
                session.Session!,
                BuildOperationalNodeIndex(),
                cts.Token);

            return new DeviceAclQueryResult(true, DeviceOperationFailure.None, response);
        }
        catch (OperationCanceledException)
        {
            return new DeviceAclQueryResult(false, DeviceOperationFailure.Timeout, null, "ACL read timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: ACL read failed", id);
            return new DeviceAclQueryResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>Reads Binding entries from all known devices on a controller fabric.</summary>
    internal async Task<FabricBindingQueryResult> ReadFabricBindingsAsync(string? fabricName = null, ushort? endpoint = null)
    {
        var requestedFabricName = string.IsNullOrWhiteSpace(fabricName)
            ? _commissioningOptions.SharedFabricName
            : fabricName;

        string requestedCompressedFabricId;
        try
        {
            var requestedFabric = await new FabricDiskStorage(Registry.BasePath).LoadFabricAsync(requestedFabricName);
            requestedCompressedFabricId = requestedFabric.CompressedFabricId;
        }
        catch (Exception ex)
        {
            return new FabricBindingQueryResult(false, DeviceOperationFailure.NotFound, null, $"Fabric {requestedFabricName} not found or unreadable: {ex.Message}");
        }

        var targetIndex = BuildOperationalNodeIndex();
        var sourceDevices = new List<DeviceInfo>();
        foreach (var device in Registry.GetAll().OrderBy(static device => device.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (await DeviceBelongsToCompressedFabricAsync(device, requestedCompressedFabricId))
            {
                sourceDevices.Add(device);
            }
        }

        var sourceResults = new List<DeviceBindingListResponse>();
        foreach (var device in sourceDevices)
        {
            var endpoints = endpoint.HasValue
                ? [endpoint.Value]
                : GetBindingEndpoints(device);

            foreach (var bindingEndpoint in endpoints)
            {
                sourceResults.Add(await ReadFabricDeviceBindingsAsync(device, bindingEndpoint, targetIndex));
            }
        }

        var successfulSources = sourceResults.Count(static result => result.Error is null);
        return new FabricBindingQueryResult(
            true,
            DeviceOperationFailure.None,
            new FabricBindingListResponse(
                requestedFabricName,
                sourceResults.Count,
                successfulSources,
                sourceResults.Count - successfulSources,
                [.. sourceResults]));
    }

    /// <summary>Reads AccessControl ACL entries from all known devices on a controller fabric.</summary>
    internal async Task<FabricAclQueryResult> ReadFabricAclsAsync(string? fabricName = null)
    {
        var requestedFabricName = string.IsNullOrWhiteSpace(fabricName)
            ? _commissioningOptions.SharedFabricName
            : fabricName;

        string requestedCompressedFabricId;
        try
        {
            var requestedFabric = await new FabricDiskStorage(Registry.BasePath).LoadFabricAsync(requestedFabricName);
            requestedCompressedFabricId = requestedFabric.CompressedFabricId;
        }
        catch (Exception ex)
        {
            return new FabricAclQueryResult(false, DeviceOperationFailure.NotFound, null, $"Fabric {requestedFabricName} not found or unreadable: {ex.Message}");
        }

        var targetIndex = BuildOperationalNodeIndex();
        var sourceResults = new List<DeviceAclListResponse>();
        foreach (var device in Registry.GetAll().OrderBy(static device => device.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (await DeviceBelongsToCompressedFabricAsync(device, requestedCompressedFabricId))
            {
                sourceResults.Add(await ReadFabricDeviceAclAsync(device, targetIndex));
            }
        }

        var successfulSources = sourceResults.Count(static result => result.Error is null);
        return new FabricAclQueryResult(
            true,
            DeviceOperationFailure.None,
            new FabricAclListResponse(
                requestedFabricName,
                sourceResults.Count,
                successfulSources,
                sourceResults.Count - successfulSources,
                [.. sourceResults]));
    }

    /// <summary>Reads raw Matter event envelopes from one device for a selected cluster and optional event.</summary>
    internal async Task<DeviceMatterEventQueryResult> ReadMatterEventsAsync(
        string id,
        ushort? endpoint,
        uint clusterId,
        uint? eventId = null,
        bool fabricFiltered = false)
    {
        var device = Registry.Get(id);
        if (device is null)
        {
            return new DeviceMatterEventQueryResult(false, DeviceOperationFailure.NotFound, null, $"Device {id} not found");
        }

        if (!TryGetConnectedSession(id, out var session))
        {
            return new DeviceMatterEventQueryResult(false, DeviceOperationFailure.NotConnected, null, $"Device {id} is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var resolvedEndpoint = ResolveMatterEventEndpoint(device, clusterId, endpoint);
            var reports = await MatterEvents.ReadAsync(
                session.Session!,
                [new MatterEventPath(resolvedEndpoint, clusterId, eventId)],
                fabricFiltered,
                cts.Token);

            Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);

            return new DeviceMatterEventQueryResult(
                true,
                DeviceOperationFailure.None,
                new DeviceMatterEventReadResponse(
                    device.Id,
                    device.Name,
                    resolvedEndpoint,
                    clusterId,
                    $"0x{clusterId:X4}",
                    eventId,
                    eventId.HasValue ? $"0x{eventId.Value:X4}" : null,
                    reports.Select(report => MapMatterEventResponse(device.Id, device.Name, report)).ToArray()));
        }
        catch (OperationCanceledException)
        {
            return new DeviceMatterEventQueryResult(false, DeviceOperationFailure.Timeout, null, "Matter event read timed out");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Device {Id}: Matter event read failed on cluster 0x{ClusterId:X4}", id, clusterId);
            return new DeviceMatterEventQueryResult(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    /// <summary>
    /// Reads and returns the latest known state for a connected device.
    /// </summary>
    public async Task<DeviceState?> ReadStateAsync(string id)
    {
        if (!Sessions.TryGetValue(id, out var rs) || rs.Session is null)
        {
            return null;
        }

        try
        {
            await ReadDeviceStateAsync(id, rs.Session);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Device {Id}: state read failed.", id);
        }

        var device = Registry.Get(id);
        return device == null ? null : new DeviceState(
            device.OnOff, device.IsOnline, device.LastSeen, device.Level, device.Hue, device.Saturation,
            device.ColorX, device.ColorY, device.ColorMode, device.VendorName, device.ProductName);
    }

    /// <inheritdoc />
    protected override bool TryScheduleManagedReconnect(string id, ResilientSession rs)
    {
        DotMatterProductDiagnostics.RecordManagedReconnectRequest();
        return base.TryScheduleManagedReconnect(id, rs);
    }

    private ushort GetEndpoint(string deviceId, uint clusterId)
    {
        var device = Registry.Get(deviceId);
        return device?.EndpointFor(clusterId) ?? 1;
    }

    private static ushort[] GetBindingEndpoints(DeviceInfo device)
    {
        if (device.Endpoints is null)
        {
            return [1];
        }

        var endpoints = device.Endpoints
            .Where(static endpoint => endpoint.Key != 0 && endpoint.Value.Contains(BindingCluster.ClusterId))
            .Select(static endpoint => endpoint.Key)
            .Order()
            .ToArray();

        return endpoints.Length == 0 ? [1] : endpoints;
    }

    private static ushort ResolveMatterEventEndpoint(DeviceInfo device, uint clusterId, ushort? requestedEndpoint)
    {
        if (requestedEndpoint.HasValue)
        {
            return requestedEndpoint.Value;
        }

        if (device.Endpoints is not null)
        {
            foreach (var (endpoint, clusters) in device.Endpoints)
            {
                if (clusters.Contains(clusterId))
                {
                    return endpoint;
                }
            }
        }

        return clusterId is AccessControlCluster.ClusterId or GeneralDiagnosticsCluster.ClusterId
            ? (ushort)0
            : (ushort)1;
    }

    private async Task<DeviceBindingListResponse> ReadFabricDeviceBindingsAsync(
        DeviceInfo device,
        ushort endpoint,
        IReadOnlyDictionary<ulong, DeviceInfo> targetIndex)
    {
        if (!TryGetConnectedSession(device.Id, out var session))
        {
            return CreateBindingErrorResponse(device, endpoint, $"Device {device.Id} is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            return await ReadBindingsForEndpointAsync(device, session.Session!, endpoint, targetIndex, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return CreateBindingErrorResponse(device, endpoint, "Binding read timed out");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Device {Id}: Binding read failed on endpoint {Endpoint}", device.Id, endpoint);
            return CreateBindingErrorResponse(device, endpoint, ex.Message);
        }
    }

    private async Task<DeviceBindingListResponse> ReadBindingsForEndpointAsync(
        DeviceInfo sourceDevice,
        ISession session,
        ushort endpoint,
        IReadOnlyDictionary<ulong, DeviceInfo> targetIndex,
        CancellationToken ct)
    {
        var binding = new BindingCluster(session, endpoint);
        var entries = await binding.ReadBindingAsync(ct) ?? [];
        return new DeviceBindingListResponse(
            sourceDevice.Id,
            sourceDevice.Name,
            sourceDevice.FabricName,
            endpoint,
            entries.Select(entry => MapBindingEntry(entry, targetIndex)).ToArray());
    }

    private async Task<DeviceAclListResponse> ReadFabricDeviceAclAsync(
        DeviceInfo device,
        IReadOnlyDictionary<ulong, DeviceInfo> targetIndex)
    {
        if (!TryGetConnectedSession(device.Id, out var session))
        {
            return CreateAclErrorResponse(device, $"Device {device.Id} is not connected");
        }

        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            return await ReadAclForEndpointAsync(device, session.Session!, targetIndex, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return CreateAclErrorResponse(device, "ACL read timed out");
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Device {Id}: ACL read failed", device.Id);
            return CreateAclErrorResponse(device, ex.Message);
        }
    }

    private static DeviceAclListResponse CreateAclErrorResponse(DeviceInfo sourceDevice, string error)
        => new(sourceDevice.Id, sourceDevice.Name, sourceDevice.FabricName, 0, [], error);

    private async Task<DeviceAclListResponse> ReadAclForEndpointAsync(
        DeviceInfo sourceDevice,
        ISession session,
        IReadOnlyDictionary<ulong, DeviceInfo> targetIndex,
        CancellationToken ct)
    {
        var accessControl = new AccessControlCluster(session, endpointId: 0);
        var entries = await accessControl.ReadACLAsync(ct) ?? [];
        return new DeviceAclListResponse(
            sourceDevice.Id,
            sourceDevice.Name,
            sourceDevice.FabricName,
            0,
            entries.Select(entry => MapAclEntry(entry, targetIndex)).ToArray());
    }

    private static DeviceBindingListResponse CreateBindingErrorResponse(DeviceInfo sourceDevice, ushort endpoint, string error)
        => new(sourceDevice.Id, sourceDevice.Name, sourceDevice.FabricName, endpoint, [], error);

    private IReadOnlyDictionary<ulong, DeviceInfo> BuildOperationalNodeIndex()
    {
        var index = new Dictionary<ulong, DeviceInfo>();
        foreach (var device in Registry.GetAll())
        {
            if (TryToOperationalNodeId(device.NodeId, out var operationalNodeId))
            {
                index[operationalNodeId] = device;
            }
        }

        return index;
    }

    private async Task<bool> DeviceBelongsToCompressedFabricAsync(DeviceInfo device, string compressedFabricId)
    {
        try
        {
            var fabric = await new FabricDiskStorage(Registry.BasePath).LoadFabricAsync(device.FabricName);
            return string.Equals(fabric.CompressedFabricId, compressedFabricId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Device {Id}: failed to read fabric {FabricName}", device.Id, device.FabricName);
            return false;
        }
    }

    private static DeviceBindingEntry MapBindingEntry(BindingCluster.TargetStruct entry, IReadOnlyDictionary<ulong, DeviceInfo> targetIndex)
    {
        DeviceInfo? targetDevice = null;
        if (entry.Node.HasValue)
        {
            targetIndex.TryGetValue(entry.Node.Value, out targetDevice);
        }

        return new DeviceBindingEntry(
            entry.Node?.ToString(CultureInfo.InvariantCulture),
            entry.Group,
            entry.Endpoint,
            entry.Cluster,
            entry.Cluster.HasValue ? $"0x{entry.Cluster.Value:X4}" : null,
            entry.FabricIndex,
            targetDevice?.Id,
            targetDevice?.Name);
    }

    private static DeviceAclEntry MapAclEntry(
        AccessControlCluster.AccessControlEntryStruct entry,
        IReadOnlyDictionary<ulong, DeviceInfo> targetIndex)
        => new(
            entry.Privilege.ToString(),
            entry.AuthMode.ToString(),
            entry.Subjects?.Select(subject => MapAclSubject(subject, targetIndex)).ToArray(),
            entry.Targets?.Select(MapAclTarget).ToArray(),
            entry.AuxiliaryType?.ToString(),
            entry.FabricIndex);

    private static DeviceAclSubject MapAclSubject(ulong subject, IReadOnlyDictionary<ulong, DeviceInfo> targetIndex)
    {
        targetIndex.TryGetValue(subject, out var device);
        return new DeviceAclSubject(
            subject.ToString(CultureInfo.InvariantCulture),
            device?.Id,
            device?.Name);
    }

    private static DeviceAclTarget MapAclTarget(AccessControlCluster.AccessControlTargetStruct target)
        => new(
            target.Cluster,
            target.Cluster.HasValue ? $"0x{target.Cluster.Value:X4}" : null,
            target.Endpoint,
            target.DeviceType,
            target.DeviceType.HasValue ? $"0x{target.DeviceType.Value:X4}" : null);

    private static MatterEventResponse MapMatterEventResponse(string deviceId, string? deviceName, MatterEventReport report)
    {
        var mappedEvent = ClusterEventRegistry.MapEventReport(report);
        return new MatterEventResponse(
            deviceId,
            deviceName,
            report.EndpointId,
            report.ClusterId,
            $"0x{report.ClusterId:X4}",
            mappedEvent?.ClusterName ?? ClusterEventRegistry.GetClusterName(report.ClusterId),
            report.EventId,
            $"0x{report.EventId:X4}",
            mappedEvent?.EventName ?? "Unknown",
            report.EventNumber,
            report.Priority,
            report.EpochTimestamp,
            report.SystemTimestamp,
            report.DeltaEpochTimestamp,
            report.DeltaSystemTimestamp,
            MapMatterEventPayload(mappedEvent, report),
            report.RawData is { } rawData ? Convert.ToHexString(rawData.GetBytes()) : null,
            report.StatusCode,
            DateTime.UtcNow);
    }

    private static MatterEventPayloadResponse MapMatterEventPayload(MatterClusterEvent? mappedEvent, MatterEventReport report)
    {
        if (mappedEvent?.TypedPayload is not null)
        {
            var payloadJson = ClusterEventRegistry.BuildPayloadJson(mappedEvent);
            if (payloadJson != null)
            {
                return new MatterEventPayloadResponse(
                    "typed",
                    ToJsonElement(payloadJson),
                    mappedEvent.Reason);
            }

            return new MatterEventPayloadResponse(
                "unknown",
                null,
                "Generated JSON projection for the typed payload is not available.");
        }

        if (mappedEvent != null)
        {
            return new MatterEventPayloadResponse("unknown", null, mappedEvent.Reason);
        }

        if (report.StatusCode.HasValue)
        {
            return new MatterEventPayloadResponse(
                "unknown",
                null,
                $"Event report carried status code 0x{report.StatusCode.Value:X2}.");
        }

        return new MatterEventPayloadResponse(
            "unknown",
            null,
            "Cluster does not have generated event support.");
    }

    private static JsonElement ToJsonElement(System.Text.Json.Nodes.JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static bool TryParseBindingNodeId(string? nodeId, out ulong? parsedNodeId, out string? error)
    {
        parsedNodeId = null;
        error = null;

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return true;
        }

        if (!ulong.TryParse(nodeId, out var parsed))
        {
            error = "Binding removal request nodeId must be an unsigned integer";
            return false;
        }

        parsedNodeId = parsed;
        return true;
    }

    private static ulong[]? ParseAclSubjects(string[]? subjects)
        => subjects?.Select(ulong.Parse).ToArray();

    private static AccessControlCluster.AccessControlTargetStruct[]? ParseAclTargets(DeviceAclRemovalTarget[]? targets)
        => targets?.Select(static target => new AccessControlCluster.AccessControlTargetStruct
        {
            Cluster = target.Cluster,
            Endpoint = target.Endpoint,
            DeviceType = target.DeviceType,
        }).ToArray();

    private static AccessControlCluster.AccessControlEntryPrivilegeEnum ParseAclPrivilege(string privilege)
        => Enum.Parse<AccessControlCluster.AccessControlEntryPrivilegeEnum>(privilege, ignoreCase: true);

    private static AccessControlCluster.AccessControlEntryAuthModeEnum ParseAclAuthMode(string authMode)
        => Enum.Parse<AccessControlCluster.AccessControlEntryAuthModeEnum>(authMode, ignoreCase: true);

    private static AccessControlCluster.AccessControlAuxiliaryTypeEnum? ParseAclAuxiliaryType(string? auxiliaryType)
        => string.IsNullOrWhiteSpace(auxiliaryType)
            ? null
            : Enum.Parse<AccessControlCluster.AccessControlAuxiliaryTypeEnum>(auxiliaryType, ignoreCase: true);

    private static ulong ToOperationalNodeId(string nodeId)
        => ToOperationalNodeId(new BigInteger(nodeId));

    private static bool TryToOperationalNodeId(string nodeId, out ulong operationalNodeId)
    {
        operationalNodeId = 0;
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        try
        {
            operationalNodeId = ToOperationalNodeId(nodeId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static ulong ToOperationalNodeId(BigInteger nodeId)
        => ulong.Parse(
            MatterDeviceHost.GetNodeOperationalId(nodeId),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

    private static async Task<WriteResponse> EnsureOnOffOperateAclAsync(
        ISession targetSession,
        ulong controllerNodeId,
        ulong switchNodeId,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var accessControl = new AccessControlCluster(targetSession, endpointId: 0);
        var existingAcl = await accessControl.ReadACLAsync(ct) ?? [];
        var desiredTarget = new AccessControlCluster.AccessControlTargetStruct
        {
            Cluster = OnOffCluster.ClusterId,
            Endpoint = targetEndpoint,
        };

        if (existingAcl.Any(entry =>
                entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate
                && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
                && entry.Subjects?.Contains(switchNodeId) == true
                && entry.Targets?.Any(target => target.Cluster == desiredTarget.Cluster && target.Endpoint == desiredTarget.Endpoint) == true))
        {
            return new WriteResponse(true, []);
        }

        var updatedAcl = existingAcl;
        if (!updatedAcl.Any(entry =>
                entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer
                && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
                && entry.Subjects?.Contains(controllerNodeId) == true))
        {
            updatedAcl = updatedAcl.Prepend(new AccessControlCluster.AccessControlEntryStruct
            {
                Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer,
                AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
                Subjects = [controllerNodeId],
                Targets = null,
            }).ToArray();
        }

        updatedAcl = updatedAcl.Append(new AccessControlCluster.AccessControlEntryStruct
        {
            Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate,
            AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
            Subjects = [switchNodeId],
            Targets = [desiredTarget],
        }).ToArray();

        return await accessControl.WriteACLAsync(updatedAcl, ct: ct);
    }

    private static async Task<WriteResponse> EnsureOnOffBindingAsync(
        ISession switchSession,
        ulong targetNodeId,
        ushort sourceEndpoint,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var binding = new BindingCluster(switchSession, sourceEndpoint);
        var existingBinding = await binding.ReadBindingAsync(ct) ?? [];

        if (existingBinding.Any(entry =>
                entry.Node == targetNodeId
                && entry.Endpoint == targetEndpoint
                && entry.Cluster == OnOffCluster.ClusterId))
        {
            return new WriteResponse(true, []);
        }

        var updatedBinding = existingBinding.Append(new BindingCluster.TargetStruct
        {
            Node = targetNodeId,
            Endpoint = targetEndpoint,
            Cluster = OnOffCluster.ClusterId,
        }).ToArray();

        return await binding.WriteBindingAsync(updatedBinding, ct: ct);
    }

    private static BindingRemovalPlan RemoveBindingEntries(
        BindingCluster.TargetStruct[] existingEntries,
        Func<BindingCluster.TargetStruct, bool> predicate)
    {
        var removedEntries = existingEntries.Where(predicate).ToArray();
        if (removedEntries.Length == 0)
        {
            return new BindingRemovalPlan(existingEntries, [], CreateAlreadyAbsentStatus(existingEntries.Length));
        }

        var updatedEntries = existingEntries.Where(entry => !predicate(entry)).ToArray();
        return new BindingRemovalPlan(updatedEntries, removedEntries, CreateRemovedStatus(removedEntries.Length, updatedEntries.Length));
    }

    private static AclRemovalPlan RemoveAclEntries(
        AccessControlCluster.AccessControlEntryStruct[] existingEntries,
        Func<AccessControlCluster.AccessControlEntryStruct, bool> predicate)
    {
        var removedEntries = existingEntries.Where(predicate).ToArray();
        if (removedEntries.Length == 0)
        {
            return new AclRemovalPlan(existingEntries, [], CreateAlreadyAbsentStatus(existingEntries.Length));
        }

        var updatedEntries = existingEntries.Where(entry => !predicate(entry)).ToArray();
        return new AclRemovalPlan(updatedEntries, removedEntries, CreateRemovedStatus(removedEntries.Length, updatedEntries.Length));
    }

    private static AclRemovalPlan RemoveOnOffAclEntries(
        AccessControlCluster.AccessControlEntryStruct[] existingEntries,
        ulong switchNodeId,
        ushort targetEndpoint)
    {
        var removedEntries = existingEntries.Where(entry => IsExactOnOffRouteAclEntry(entry, switchNodeId, targetEndpoint)).ToArray();
        if (removedEntries.Length > 0)
        {
            var updatedEntries = existingEntries.Where(entry => !IsExactOnOffRouteAclEntry(entry, switchNodeId, targetEndpoint)).ToArray();
            return new AclRemovalPlan(updatedEntries, removedEntries, CreateRemovedStatus(removedEntries.Length, updatedEntries.Length));
        }

        if (existingEntries.Any(entry => EntryMayAuthorizeOnOffRoute(entry, switchNodeId, targetEndpoint)))
        {
            return new AclRemovalPlan(
                existingEntries,
                [],
                CreatePreservedStatus(existingEntries.Length, "Broader or manual ACL state still covers this route"));
        }

        return new AclRemovalPlan(existingEntries, [], CreateAlreadyAbsentStatus(existingEntries.Length));
    }

    private static bool MatchesBindingRemovalRequest(
        BindingCluster.TargetStruct entry,
        ulong? nodeId,
        DeviceBindingRemovalRequest request)
    {
        if (nodeId.HasValue && entry.Node != nodeId.Value)
        {
            return false;
        }

        if (request.Group.HasValue && entry.Group != request.Group.Value)
        {
            return false;
        }

        if (request.TargetEndpoint.HasValue && entry.Endpoint != request.TargetEndpoint.Value)
        {
            return false;
        }

        if (request.Cluster.HasValue && entry.Cluster != request.Cluster.Value)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesAclRemovalRequest(
        AccessControlCluster.AccessControlEntryStruct entry,
        AccessControlCluster.AccessControlEntryPrivilegeEnum privilege,
        AccessControlCluster.AccessControlEntryAuthModeEnum authMode,
        ulong[]? subjects,
        AccessControlCluster.AccessControlTargetStruct[]? targets,
        AccessControlCluster.AccessControlAuxiliaryTypeEnum? auxiliaryType)
    {
        if (entry.Privilege != privilege || entry.AuthMode != authMode)
        {
            return false;
        }

        if (auxiliaryType.HasValue && entry.AuxiliaryType != auxiliaryType)
        {
            return false;
        }

        if (subjects is not null && !AclSubjectsExactlyMatch(entry.Subjects, subjects))
        {
            return false;
        }

        if (targets is not null && !AclTargetsExactlyMatch(entry.Targets, targets))
        {
            return false;
        }

        return true;
    }

    private static bool IsExactOnOffRouteAclEntry(
        AccessControlCluster.AccessControlEntryStruct entry,
        ulong switchNodeId,
        ushort targetEndpoint)
        => entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate
            && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
            && entry.AuxiliaryType is null
            && AclSubjectsExactlyMatch(entry.Subjects, [switchNodeId])
            && AclTargetsExactlyMatch(entry.Targets, [new AccessControlCluster.AccessControlTargetStruct
            {
                Cluster = OnOffCluster.ClusterId,
                Endpoint = targetEndpoint,
            }]);

    private static bool EntryMayAuthorizeOnOffRoute(
        AccessControlCluster.AccessControlEntryStruct entry,
        ulong switchNodeId,
        ushort targetEndpoint)
    {
        if (entry.AuthMode != AccessControlCluster.AccessControlEntryAuthModeEnum.CASE)
        {
            return false;
        }

        if (entry.Privilege < AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate)
        {
            return false;
        }

        if (entry.Subjects is { Length: > 0 } && !entry.Subjects.Contains(switchNodeId))
        {
            return false;
        }

        if (entry.Targets is null || entry.Targets.Length == 0)
        {
            return true;
        }

        return entry.Targets.Any(target => TargetMayAuthorizeOnOffRoute(target, targetEndpoint));
    }

    private static bool TargetMayAuthorizeOnOffRoute(
        AccessControlCluster.AccessControlTargetStruct target,
        ushort targetEndpoint)
    {
        var clusterMatches = !target.Cluster.HasValue || target.Cluster.Value == OnOffCluster.ClusterId;
        var endpointMatches = !target.Endpoint.HasValue || target.Endpoint.Value == targetEndpoint;
        return clusterMatches && endpointMatches;
    }

    private static bool AclSubjectsExactlyMatch(ulong[]? existingSubjects, ulong[] requestedSubjects)
    {
        if (requestedSubjects.Length == 0)
        {
            return existingSubjects is null || existingSubjects.Length == 0;
        }

        return existingSubjects is not null
            && existingSubjects.Order().SequenceEqual(requestedSubjects.Order());
    }

    private static bool AclTargetsExactlyMatch(
        AccessControlCluster.AccessControlTargetStruct[]? existingTargets,
        AccessControlCluster.AccessControlTargetStruct[] requestedTargets)
    {
        if (requestedTargets.Length == 0)
        {
            return existingTargets is null || existingTargets.Length == 0;
        }

        return existingTargets is not null
            && existingTargets
                .Select(NormalizeAclTarget)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(requestedTargets.Select(NormalizeAclTarget).Order(StringComparer.Ordinal));
    }

    private static string NormalizeAclTarget(AccessControlCluster.AccessControlTargetStruct target)
        => $"{target.Cluster?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{target.Endpoint?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{target.DeviceType?.ToString(CultureInfo.InvariantCulture) ?? "null"}";

    private static RemovalStatus CreateRemovedStatus(int removedCount, int remainingCount)
        => new("removed", removedCount, remainingCount);

    private static RemovalStatus CreateAlreadyAbsentStatus(int remainingCount)
        => new("alreadyAbsent", 0, remainingCount);

    private static RemovalStatus CreatePreservedStatus(int remainingCount, string reason)
        => new("preserved", 0, remainingCount, reason);

    private static RemovalStatus CreateNotAttemptedStatus(int remainingCount, string reason)
        => new("notAttempted", 0, remainingCount, reason);

    private static RemovalStatus CreateWriteFailedStatus(int remainingCount, string reason)
        => new("writeFailed", 0, remainingCount, reason);

    private static string FormatWriteResponse(WriteResponse response)
    {
        if (response.StatusCode is { } statusCode)
        {
            return $"status=0x{statusCode:X2}";
        }

        if (response.AttributeStatuses.Count == 0)
        {
            return "no write status returned";
        }

        return string.Join(", ", response.AttributeStatuses.Select(
            status => $"attr=0x{status.AttributeId:X4} status=0x{status.StatusCode:X2}"));
    }

    private bool TryGetConnectedSession(string id, out ResilientSession session)
    {
        if (Sessions.TryGetValue(id, out var currentSession) && currentSession.Session is not null)
        {
            session = currentSession;
            return true;
        }

        session = default!;
        return false;
    }
}
