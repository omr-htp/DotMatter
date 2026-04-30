using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using Microsoft.Extensions.Logging;

namespace DotMatter.Hosting;

internal sealed class AttributeReportProjector(
    DeviceRegistry registry,
    ILogger log,
    Action<string, string, string, string> onStateChanged)
{
    public void Process(string id, ReportDataAction report)
    {
        foreach (var attributeReport in report.AttributeReports)
        {
            var attributeData = attributeReport.AttributeData;
            if (attributeData is null)
            {
                continue;
            }

            var cluster = attributeData.Path.ClusterId;
            var attribute = attributeData.Path.AttributeId;
            var endpointId = $"ep{attributeData.Path.EndpointId}";

            if (cluster == OnOffCluster.ClusterId && attribute == OnOffCluster.Attributes.OnOff && attributeData.Data is bool onOff)
            {
                registry.Update(id, device => { device.OnOff = onOff; device.LastSeen = DateTime.UtcNow; });
                log.LogInformation("[MATTER] {Id}: OnOff -> {State}", id, onOff ? "ON" : "OFF");
                onStateChanged(id, endpointId, "on_off", onOff.ToString().ToLowerInvariant());
                continue;
            }

            if (cluster == LevelControlCluster.ClusterId && attribute == LevelControlCluster.Attributes.CurrentLevel)
            {
                var level = Convert.ToByte(attributeData.Data);
                registry.Update(id, device => { device.Level = level; device.LastSeen = DateTime.UtcNow; });
                log.LogInformation("[MATTER] {Id}: Level -> {Level}", id, level);
                onStateChanged(id, endpointId, "level", level.ToString());
                continue;
            }

            if (cluster == ColorControlCluster.ClusterId)
            {
                if (TryProjectColorAttribute(id, endpointId, attribute, attributeData.Data))
                {
                    continue;
                }
            }

            log.LogDebug("[MATTER] {Id}: unhandled report cluster=0x{Cluster:X4} attr=0x{Attr:X4} data={Data}",
                id, cluster, attribute, attributeData.Data);
        }
    }

    private bool TryProjectColorAttribute(string id, string endpointId, uint attribute, object? value)
    {
        if (attribute == ColorControlCluster.Attributes.CurrentHue)
        {
            var hue = Convert.ToByte(value);
            registry.Update(id, device => { device.Hue = hue; device.LastSeen = DateTime.UtcNow; });
            log.LogInformation("[MATTER] {Id}: Hue -> {Hue}", id, hue);
            onStateChanged(id, endpointId, "hue", hue.ToString());
            return true;
        }

        if (attribute == ColorControlCluster.Attributes.CurrentSaturation)
        {
            var saturation = Convert.ToByte(value);
            registry.Update(id, device => { device.Saturation = saturation; device.LastSeen = DateTime.UtcNow; });
            log.LogInformation("[MATTER] {Id}: Saturation -> {Sat}", id, saturation);
            onStateChanged(id, endpointId, "saturation", saturation.ToString());
            return true;
        }

        if (attribute == ColorControlCluster.Attributes.ColorTemperatureMireds)
        {
            var mireds = Convert.ToUInt16(value);
            registry.Update(id, device => { device.ColorTempMireds = mireds; device.LastSeen = DateTime.UtcNow; });
            log.LogInformation("[MATTER] {Id}: ColorTemp -> {Mireds} mireds", id, mireds);
            onStateChanged(id, endpointId, "color_temp", mireds.ToString());
            return true;
        }

        if (attribute == ColorControlCluster.Attributes.CurrentX)
        {
            var x = Convert.ToUInt16(value);
            registry.Update(id, device => { device.ColorX = x; device.LastSeen = DateTime.UtcNow; });
            log.LogDebug("[MATTER] {Id}: CurrentX -> {X}", id, x);
            PublishDerivedHueSaturation(id, endpointId, colorX: x, colorY: registry.Get(id)?.ColorY);
            return true;
        }

        if (attribute == ColorControlCluster.Attributes.CurrentY)
        {
            var y = Convert.ToUInt16(value);
            registry.Update(id, device => { device.ColorY = y; device.LastSeen = DateTime.UtcNow; });
            log.LogDebug("[MATTER] {Id}: CurrentY -> {Y}", id, y);
            PublishDerivedHueSaturation(id, endpointId, colorX: registry.Get(id)?.ColorX, colorY: y);
            return true;
        }

        return false;
    }

    private void PublishDerivedHueSaturation(string id, string endpointId, ushort? colorX, ushort? colorY)
    {
        if (!colorX.HasValue || !colorY.HasValue)
        {
            return;
        }

        var (hue, saturation) = ColorConversion.XYToHueSat(colorX.Value, colorY.Value);
        registry.Update(id, device => { device.Hue = hue; device.Saturation = saturation; });
        onStateChanged(id, endpointId, "hue", hue.ToString());
        onStateChanged(id, endpointId, "saturation", saturation.ToString());
    }

}
