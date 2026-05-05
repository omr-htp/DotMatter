using System.Collections.Concurrent;
using DotMatter.Core;
using DotMatter.Core.Fabrics;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Hosting.Devices;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Math;

namespace DotMatter.Hosting.Runtime;

internal sealed class SessionLifecycleCoordinator(
    ILogger log,
    DeviceRegistry registry,
    ConcurrentDictionary<string, DateTime> lastPing,
    ConcurrentDictionary<string, DateTime> lastSubscriptionReport,
    ConcurrentDictionary<string, TimeSpan> subscriptionStaleThresholds,
    ConcurrentDictionary<string, Subscription> subscriptions,
    DeviceStateProbe stateProbe,
    SubscriptionCoordinator subscriptionCoordinator,
    SessionRecoveryOptions recoveryOptions,
    Action<string> onDeviceDisconnected,
    Func<string, ISession, Task> onDeviceConnectedAsync,
    Func<CancellationToken> getServiceToken,
    Func<string, Func<CancellationToken, Task>, bool> scheduleOwnedOperation)
{
    public async Task<ResilientSession?> CreateAsync(string id)
    {
        var device = registry.Get(id);
        if (device == null)
        {
            return null;
        }

        IFabricStorageProvider storageProvider = new FabricDiskStorage(registry.BasePath);
        var fabricManager = new FabricManager(storageProvider);
        var fabric = await fabricManager.GetAsync(device.FabricName);

        var nodeId = new BigInteger(device.NodeId);
        var nodeOperationalId = MatterDeviceHost.GetNodeOperationalId(nodeId);
        var storedIp = string.IsNullOrEmpty(device.Ip)
            ? System.Net.IPAddress.IPv6Loopback
            : System.Net.IPAddress.Parse(device.Ip);

        var resilientSession = new ResilientSession(
            fabric,
            nodeId,
            fabric.CompressedFabricId,
            nodeOperationalId,
            storedIp,
            (ushort)device.Port,
            maxRetries: 3);

        resilientSession.IpDiscovered += (ip, port) =>
        {
            var ipString = ip.ToString();
            registry.Update(id, d => { d.Ip = ipString; d.Port = port; });
            registry.PersistIp(id, ipString);
        };

        resilientSession.Connected += async (session, _) =>
        {
            lastPing[id] = DateTime.UtcNow;
            registry.Update(id, d => { d.IsOnline = true; d.LastSeen = DateTime.UtcNow; });
            log.LogInformation("[MATTER] {Id}: CASE session established", id);

            var serviceToken = getServiceToken();
            await RunBoundedStepAsync(id, "endpoint discovery", recoveryOptions.EndpointDiscoveryTimeout,
                innerCt => stateProbe.DiscoverDeviceEndpointsAsync(id, session, innerCt), serviceToken);

            await RunBoundedStepAsync(id, "state read", recoveryOptions.StateReadTimeout,
                innerCt => stateProbe.ReadDeviceStateAsync(id, session, innerCt), serviceToken);

            await RunBoundedStepAsync(id, "subscription", recoveryOptions.SubscriptionSetupTimeout,
                innerCt => subscriptionCoordinator.StartAsync(id, session, innerCt), serviceToken);

            await onDeviceConnectedAsync(id, session);
        };

        resilientSession.Disconnected += () =>
        {
            if (subscriptions.TryRemove(id, out var subscription))
            {
                scheduleOwnedOperation($"dispose-subscription:{id}", async _ => await subscription.DisposeAsync().AsTask());
            }

            lastPing.TryRemove(id, out _);
            lastSubscriptionReport.TryRemove(id, out _);
            subscriptionStaleThresholds.TryRemove(id, out _);
            registry.Update(id, d => d.IsOnline = false);
            onDeviceDisconnected(id);
        };

        return resilientSession;
    }

    private async Task RunBoundedStepAsync(
        string id,
        string stepName,
        TimeSpan timeout,
        Func<CancellationToken, Task> action,
        CancellationToken serviceToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
            cts.CancelAfter(timeout);
            await action(cts.Token).WaitAsync(cts.Token);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[MATTER] {Id}: {Step} failed", id, stepName);
        }
    }
}
