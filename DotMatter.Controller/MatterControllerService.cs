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
    private readonly ControllerApiOptions _apiOptions = apiOptions.Value;
    private readonly CommissioningOptions _commissioningOptions = commissioningOptions.Value;
    private readonly DeviceCommandExecutor _commandExecutor = new(log, registry, apiOptions);
    private int _nextClientId;

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

    /// <summary>Creates a per-client SSE channel.</summary>
    public ControllerEventSubscription SubscribeEvents(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(_apiOptions.SseClientBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var clientId = Interlocked.Increment(ref _nextClientId);
        _sseClients[clientId] = channel;
        var registration = ct.Register(() => RemoveSseClient(clientId));

        return new ControllerEventSubscription(
            channel.Reader,
            () =>
            {
                registration.Dispose();
                RemoveSseClient(clientId);
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

    private void BroadcastEvent(string evt)
    {
        foreach (var (_, ch) in _sseClients)
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
        DotMatterProductDiagnostics.SubscriptionRestarts.Add(1);
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
    public override async Task StopAsync(CancellationToken ct)
    {
        foreach (var clientId in _sseClients.Keys.ToArray())
        {
            RemoveSseClient(clientId);
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
        DotMatterProductDiagnostics.ManagedReconnectRequests.Add(1);
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
