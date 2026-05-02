using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq;

namespace DotMatter.Hosting;

internal sealed class SubscriptionCoordinator(
    ILogger log,
    DeviceRegistry registry,
    ConcurrentDictionary<string, Subscription> subscriptions,
    ConcurrentDictionary<string, DateTime> lastSubscriptionReport,
    Action<string, ReportDataAction> processAttributeReports,
    Action<string, IReadOnlyList<MatterEventReport>> processEventReports)
{
    public async Task StartAsync(string id, ISession session, CancellationToken ct = default)
    {
        var device = registry.Get(id);
        var supportsHueSaturation = device?.SupportsHueSaturation ?? false;
        var supportsXY = device?.SupportsXY ?? false;
        var supportsColorTemperature = (device?.SupportsCluster(ColorControlCluster.ClusterId) ?? true)
            && (device?.SupportsColorTemperature ?? true);

        var onOffEndpoint = device?.EndpointFor(OnOffCluster.ClusterId) ?? 1;
        var levelEndpoint = device?.EndpointFor(LevelControlCluster.ClusterId) ?? 1;
        var colorEndpoint = device?.EndpointFor(ColorControlCluster.ClusterId) ?? 1;
        List<AttributePath> paths = [];
        if (device?.SupportsCluster(OnOffCluster.ClusterId) ?? true)
        {
            paths.Add(new(onOffEndpoint, OnOffCluster.ClusterId, OnOffCluster.Attributes.OnOff));
        }

        if (device?.SupportsCluster(LevelControlCluster.ClusterId) ?? true)
        {
            paths.Add(new(levelEndpoint, LevelControlCluster.ClusterId, LevelControlCluster.Attributes.CurrentLevel));
        }

        if (supportsHueSaturation)
        {
            paths.Add(new(colorEndpoint, ColorControlCluster.ClusterId, ColorControlCluster.Attributes.CurrentHue));
            paths.Add(new(colorEndpoint, ColorControlCluster.ClusterId, ColorControlCluster.Attributes.CurrentSaturation));
        }
        else if (supportsXY)
        {
            paths.Add(new(colorEndpoint, ColorControlCluster.ClusterId, ColorControlCluster.Attributes.CurrentX));
            paths.Add(new(colorEndpoint, ColorControlCluster.ClusterId, ColorControlCluster.Attributes.CurrentY));
        }

        if (supportsColorTemperature)
        {
            paths.Add(new(colorEndpoint, ColorControlCluster.ClusterId, ColorControlCluster.Attributes.ColorTemperatureMireds));
        }

        var eventPaths = BuildEventPaths(device);

        if (paths.Count == 0 && eventPaths.Count == 0)
        {
            log.LogInformation("[MATTER] {Id}: no subscribable attributes or events discovered", id);
            return;
        }

        try
        {
            var subscription = await Subscription.CreateMultiAsync(
                session,
                paths,
                eventPaths,
                minInterval: 1,
                maxInterval: 1,
                keepSubscriptions: false,
                ct: ct);

            subscription.OnReport += report =>
            {
                lastSubscriptionReport[id] = DateTime.UtcNow;
                if (report.AttributeReports.Count == 0 && report.EventReports.Count == 0)
                {
                    log.LogDebug("[MATTER] {Id}: subscription report attr=0 event=0", id);
                    return;
                }

                log.LogInformation("[MATTER] {Id}: subscription report attr={AttributeCount} event={EventCount}",
                    id, report.AttributeReports.Count, report.EventReports.Count);
                if (report.AttributeReports.Count > 0)
                {
                    processAttributeReports(id, report);
                }
            };

            subscription.OnEvent += reports =>
            {
                log.LogInformation("[MATTER] {Id}: event details {Events}",
                    id,
                    string.Join(", ", reports.Select(report => $"0x{report.EventData?.ClusterId ?? 0:X4}/0x{report.EventData?.EventId ?? 0:X4}")));
                processEventReports(id, MatterEventReport.FromReports(reports));
            };

            subscription.OnTerminated += ex =>
            {
                log.LogWarning(ex, "[MATTER] {Id}: subscription terminated", id);
                subscriptions.TryRemove(id, out _);
            };

            subscriptions[id] = subscription;
            lastSubscriptionReport[id] = DateTime.UtcNow;
            log.LogInformation("[MATTER] {Id}: subscribed to {AttributeCount} attribute path(s), {EventCount} event path(s)",
                id, paths.Count, eventPaths.Count);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[MATTER] {Id}: subscription failed", id);
            throw;
        }
    }

    private static List<EventPath> BuildEventPaths(DeviceInfo? device)
    {
        var eventPaths = new List<EventPath>();
        var seen = new HashSet<(ushort EndpointId, uint ClusterId)>();

        if (device?.Endpoints is { Count: > 0 } endpoints)
        {
            foreach (var (endpointId, clusters) in endpoints.OrderBy(static entry => entry.Key))
            {
                foreach (var clusterId in clusters)
                {
                    if (!ClusterEventRegistry.SupportsCluster(clusterId))
                    {
                        continue;
                    }

                    if (seen.Add((endpointId, clusterId)))
                    {
                        eventPaths.Add(new EventPath(endpointId, clusterId));
                    }
                }
            }

            return eventPaths;
        }

        AddFallbackPath(eventPaths, seen, device, SwitchCluster.ClusterId, rootEndpoint: false);
        AddFallbackPath(eventPaths, seen, device, AccessControlCluster.ClusterId, rootEndpoint: true);
        AddFallbackPath(eventPaths, seen, device, GeneralDiagnosticsCluster.ClusterId, rootEndpoint: true);
        AddFallbackPath(eventPaths, seen, device, DoorLockCluster.ClusterId, rootEndpoint: false);
        return eventPaths;
    }

    private static void AddFallbackPath(
        List<EventPath> eventPaths,
        HashSet<(ushort EndpointId, uint ClusterId)> seen,
        DeviceInfo? device,
        uint clusterId,
        bool rootEndpoint)
    {
        if (!ClusterEventRegistry.SupportsCluster(clusterId) || !(device?.SupportsCluster(clusterId) ?? true))
        {
            return;
        }

        var endpointId = rootEndpoint ? (ushort)0 : device?.EndpointFor(clusterId) ?? 1;
        if (seen.Add((endpointId, clusterId)))
        {
            eventPaths.Add(new EventPath(endpointId, clusterId));
        }
    }
}
