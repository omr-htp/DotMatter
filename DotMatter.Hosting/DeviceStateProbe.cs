using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace DotMatter.Hosting;

internal sealed class DeviceStateProbe(
    DeviceRegistry registry,
    ILogger log,
    Action<string, string, string, string> onStateChanged)
{
    public async Task DiscoverDeviceEndpointsAsync(string id, ISession session, CancellationToken ct = default)
    {
        var endpoints = await InteractionManager.DiscoverEndpointsAsync(session, ct);
        var map = new Dictionary<ushort, List<uint>>();
        foreach (var endpoint in endpoints)
        {
            var clusters = await InteractionManager.ReadServerListAsync(session, endpoint, ct);
            map[endpoint] = [.. clusters];
        }

        registry.Update(id, device => device.Endpoints = map);
        log.LogInformation("[MATTER] {Id}: discovered {Count} endpoint(s): {Endpoints}",
            id,
            map.Count,
            string.Join(", ", map.Select(kv =>
                $"EP{kv.Key}({string.Join("/", kv.Value.Select(cluster => $"0x{cluster:X4}"))})")));
    }

    public async Task ReadDeviceStateAsync(string id, ISession session, CancellationToken ct = default)
    {
        var device = registry.Get(id);
        var endpoint = device?.EndpointFor(OnOffCluster.ClusterId) ?? 1;
        var endpointId = $"ep{endpoint}";

        if (device?.SupportsCluster(OnOffCluster.ClusterId) ?? true)
        {
            try
            {
                var onOff = await new OnOffCluster(session, endpoint).ReadOnOffAsync(ct);
                registry.Update(id, d => { d.OnOff = onOff; d.LastSeen = DateTime.UtcNow; });
                onStateChanged(id, endpointId, "on_off", onOff.ToString().ToLowerInvariant());
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "[MATTER] ReadState {Id} OnOff failed", id);
            }
        }

        if (device?.SupportsCluster(LevelControlCluster.ClusterId) ?? true)
        {
            try
            {
                var level = await new LevelControlCluster(session, device?.EndpointFor(LevelControlCluster.ClusterId) ?? endpoint)
                    .ReadCurrentLevelAsync(ct);
                registry.Update(id, d => { d.Level = level; d.LastSeen = DateTime.UtcNow; });
                if (level.HasValue)
                {
                    onStateChanged(id, endpointId, "level", level.Value.ToString());
                }
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "[MATTER] ReadState {Id} Level failed", id);
            }
        }

        var colorEndpoint = device?.EndpointFor(ColorControlCluster.ClusterId) ?? endpoint;
        var color = new ColorControlCluster(session, colorEndpoint);

        if (device?.SupportsCluster(ColorControlCluster.ClusterId) ?? true)
        {
            try
            {
                var capabilities = await color.ReadColorCapabilitiesAsync(ct);
                registry.Update(id, d => d.ColorCapabilities = capabilities);
                log.LogInformation("[MATTER] {Id}: ColorCaps=0x{Capabilities:X2} ({Description})",
                    id, (ushort)capabilities, capabilities);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "[MATTER] ReadState {Id} ColorCaps failed", id);
            }
        }

        device = registry.Get(id);
        if (device?.SupportsHueSaturation ?? false)
        {
            await ReadHueSaturationAsync(id, endpointId, color, ct);
        }
        else if (device?.SupportsXY ?? false)
        {
            await ReadXYAsync(id, endpointId, color, ct);
        }

        if (device?.SupportsColorTemperature ?? false)
        {
            try
            {
                var mireds = await color.ReadColorTemperatureMiredsAsync(ct);
                registry.Update(id, d => { d.ColorTempMireds = mireds; d.LastSeen = DateTime.UtcNow; });
                onStateChanged(id, endpointId, "color_temp", mireds.ToString());
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "[MATTER] ReadState {Id} ColorTemp failed", id);
            }
        }

    }

    private async Task ReadHueSaturationAsync(
        string id,
        string endpointId,
        ColorControlCluster color,
        CancellationToken ct)
    {
        try
        {
            var hue = await color.ReadCurrentHueAsync(ct);
            var saturation = await color.ReadCurrentSaturationAsync(ct);
            registry.Update(id, device =>
            {
                device.Hue = hue;
                device.Saturation = saturation;
                device.LastSeen = DateTime.UtcNow;
            });
            onStateChanged(id, endpointId, "hue", hue.ToString());
            onStateChanged(id, endpointId, "saturation", saturation.ToString());
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "[MATTER] ReadState {Id} HueSat failed", id);
        }
    }

    private async Task ReadXYAsync(
        string id,
        string endpointId,
        ColorControlCluster color,
        CancellationToken ct)
    {
        try
        {
            var x = await color.ReadCurrentXAsync(ct);
            var y = await color.ReadCurrentYAsync(ct);
            registry.Update(id, device => { device.ColorX = x; device.ColorY = y; device.LastSeen = DateTime.UtcNow; });

            var (hue, saturation) = ColorConversion.XYToHueSat(x, y);
            registry.Update(id, device =>
            {
                device.Hue = hue;
                device.Saturation = saturation;
            });
            onStateChanged(id, endpointId, "hue", hue.ToString());
            onStateChanged(id, endpointId, "saturation", saturation.ToString());
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "[MATTER] ReadState {Id} XY failed", id);
        }
    }
}
