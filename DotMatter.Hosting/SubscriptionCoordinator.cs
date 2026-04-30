using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DotMatter.Hosting;

internal sealed class SubscriptionCoordinator(
    ILogger log,
    DeviceRegistry registry,
    ConcurrentDictionary<string, Subscription> subscriptions,
    ConcurrentDictionary<string, DateTime> lastSubscriptionReport,
    Action<string, ReportDataAction> processAttributeReports)
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

        List<EventPath> eventPaths = [];

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
                minInterval: 3,
                maxInterval: 30,
                keepSubscriptions: false,
                ct: ct);

            subscription.OnReport += report =>
            {
                lastSubscriptionReport[id] = DateTime.UtcNow;
                processAttributeReports(id, report);
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
        }
    }
}
