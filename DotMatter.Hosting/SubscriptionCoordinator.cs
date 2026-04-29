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
        var supportsColorTemperature = device?.SupportsColorTemperature ?? true;

        var onOffEndpoint = device?.EndpointFor(OnOffCluster.ClusterId) ?? 1;
        var levelEndpoint = device?.EndpointFor(LevelControlCluster.ClusterId) ?? 1;
        var colorEndpoint = device?.EndpointFor(ColorControlCluster.ClusterId) ?? 1;

        List<AttributePath> paths =
        [
            new(onOffEndpoint, OnOffCluster.ClusterId, OnOffCluster.Attributes.OnOff),
            new(levelEndpoint, LevelControlCluster.ClusterId, LevelControlCluster.Attributes.CurrentLevel),
        ];

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

        try
        {
            var subscription = await Subscription.CreateMultiAsync(
                session,
                paths,
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
            log.LogInformation("[MATTER] {Id}: subscribed to OnOff, Level, Color", id);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[MATTER] {Id}: subscription failed", id);
        }
    }
}
