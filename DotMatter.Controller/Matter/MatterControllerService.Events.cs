using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DotMatter.Controller.Diagnostics;
using DotMatter.Core;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Fabrics;
using DotMatter.Hosting;
using DotMatter.Hosting.Commissioning;
using DotMatter.Hosting.Storage;
using ISession = DotMatter.Core.Sessions.ISession;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
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
        await MigrateSharedFabricDeviceMetadataAsync(ct);

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

    private async Task MigrateSharedFabricDeviceMetadataAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Registry.BasePath))
        {
            return;
        }

        var sharedFabricName = MatterFabricNames.Normalize(_commissioningOptions.SharedFabricName);
        var sharedFabricPath = MatterFabricNames.GetFabricPath(Registry.BasePath, sharedFabricName);
        if (!File.Exists(Path.Combine(sharedFabricPath, "fabric.json")))
        {
            return;
        }

        var fabricStorage = new FabricDiskStorage(Registry.BasePath);
        Fabric sharedFabric;
        try
        {
            sharedFabric = await fabricStorage.LoadFabricAsync(sharedFabricName);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Failed to load shared Matter fabric {FabricName}; skipping legacy metadata migration.", sharedFabricName);
            return;
        }

        foreach (var fabricDir in Directory.GetDirectories(Registry.BasePath))
        {
            ct.ThrowIfCancellationRequested();

            var legacyFabricName = Path.GetFileName(fabricDir);
            if (string.Equals(legacyFabricName, sharedFabricName, StringComparison.Ordinal))
            {
                continue;
            }

            var nodeInfoPath = Path.Combine(fabricDir, "node_info.json");
            if (!File.Exists(nodeInfoPath) || !File.Exists(Path.Combine(fabricDir, "fabric.json")))
            {
                continue;
            }

            try
            {
                var legacyFabric = await fabricStorage.LoadFabricAsync(legacyFabricName);
                if (!string.Equals(legacyFabric.CompressedFabricId, sharedFabric.CompressedFabricId, StringComparison.OrdinalIgnoreCase))
                {
                    Log.LogWarning(
                        "Skipping legacy fabric directory {FabricName}; compressed fabric {CompressedFabricId} does not match shared fabric {SharedCompressedFabricId}.",
                        legacyFabricName,
                        legacyFabric.CompressedFabricId,
                        sharedFabric.CompressedFabricId);
                    continue;
                }

                var nodeInfo = JsonSerializer.Deserialize(
                    File.ReadAllText(nodeInfoPath).TrimStart('\uFEFF'),
                    HostingJsonContext.Default.NodeInfoRecord);
                if (nodeInfo is null || string.IsNullOrWhiteSpace(nodeInfo.NodeId))
                {
                    continue;
                }

                var migrated = nodeInfo with
                {
                    FabricName = sharedFabricName,
                    DeviceName = string.IsNullOrWhiteSpace(nodeInfo.DeviceName) ? legacyFabricName : nodeInfo.DeviceName
                };

                await MatterCommissioningStorage.WriteDeviceNodeInfoAsync(
                    Registry.BasePath,
                    sharedFabricName,
                    legacyFabricName,
                    migrated,
                    ct);

                Directory.Delete(fabricDir, recursive: true);
                Log.LogInformation("Migrated legacy device {DeviceId} into shared fabric {SharedFabricName}.", legacyFabricName, sharedFabricName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.LogWarning(ex, "Failed to migrate legacy fabric directory {FabricName}.", legacyFabricName);
            }
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
}
