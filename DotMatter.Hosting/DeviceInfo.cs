using DotMatter.Core.Clusters;

namespace DotMatter.Hosting;

/// <summary>
/// Stores commissioned device identity, runtime state, metadata, and discovered endpoints.
/// </summary>
public class DeviceInfo
{
    private readonly Lock _lock = new();

    /// <summary>Gets or sets the stable controller-local device identifier.</summary>
    public string Id { get; set; } = "";
    /// <summary>Gets or sets the display name for the device.</summary>
    public string Name { get; set; } = "";
    /// <summary>Gets or sets the Matter node identifier.</summary>
    public string NodeId { get; set; } = "";
    /// <summary>Gets or sets the current operational IP address.</summary>
    public string Ip { get; set; } = "";
    /// <summary>Gets or sets the current operational port.</summary>
    public int Port { get; set; } = 5540;
    /// <summary>Gets or sets the fabric directory name associated with the device.</summary>
    public string FabricName { get; set; } = "";
    /// <summary>Gets or sets the transport used during commissioning or discovery.</summary>
    public string? Transport { get; set; }

    /// <summary>Gets or sets a value indicating whether the controller currently considers the device online.</summary>
    public bool IsOnline { get; set; }
    /// <summary>Gets or sets the last reported On/Off state.</summary>
    public bool? OnOff { get; set; }
    /// <summary>Gets or sets the last reported level.</summary>
    public byte? Level { get; set; }
    /// <summary>Gets or sets the last reported hue.</summary>
    public byte? Hue { get; set; }
    /// <summary>Gets or sets the last reported saturation.</summary>
    public byte? Saturation { get; set; }
    /// <summary>Gets or sets the last reported CIE X color coordinate.</summary>
    public ushort? ColorX { get; set; }
    /// <summary>Gets or sets the last reported CIE Y color coordinate.</summary>
    public ushort? ColorY { get; set; }
    /// <summary>Gets or sets the last reported Matter color mode.</summary>
    public byte? ColorMode { get; set; }
    /// <summary>Gets or sets the last reported color temperature in mireds.</summary>
    public ushort? ColorTempMireds { get; set; }
    /// <summary>Gets or sets the most recent time the controller received device state.</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>Gets or sets the vendor name reported by the device.</summary>
    public string? VendorName { get; set; }
    /// <summary>Gets or sets the product name reported by the device.</summary>
    public string? ProductName { get; set; }
    /// <summary>Gets or sets the controller-facing device type label.</summary>
    public string DeviceType { get; set; } = "on_off_light";

    /// <summary>Gets or sets the supported color-control feature bitmap.</summary>
    public ColorControlCluster.ColorCapabilitiesBitmap ColorCapabilities { get; set; }
    /// <summary>Gets a value indicating whether hue/saturation color control is supported.</summary>
    public bool SupportsHueSaturation => ColorCapabilities.HasFlag(ColorControlCluster.ColorCapabilitiesBitmap.HueSaturation);
    /// <summary>Gets a value indicating whether enhanced hue color control is supported.</summary>
    public bool SupportsEnhancedHue => ColorCapabilities.HasFlag(ColorControlCluster.ColorCapabilitiesBitmap.EnhancedHue);
    /// <summary>Gets a value indicating whether CIE XY color control is supported.</summary>
    public bool SupportsXY => ColorCapabilities.HasFlag(ColorControlCluster.ColorCapabilitiesBitmap.XY);
    /// <summary>Gets a value indicating whether color-temperature control is supported.</summary>
    public bool SupportsColorTemperature => ColorCapabilities.HasFlag(ColorControlCluster.ColorCapabilitiesBitmap.ColorTemperature);

    /// <summary>Gets or sets the discovered endpoint-to-cluster mapping. Null until populated.</summary>
    public Dictionary<ushort, List<uint>>? Endpoints { get; set; }

    /// <summary>Returns the first non-root endpoint that hosts the given cluster, or 1 as fallback.</summary>
    public ushort EndpointFor(uint clusterId)
    {
        if (Endpoints != null)
        {
            foreach (var (ep, clusters) in Endpoints)
            {
                if (ep == 0)
                {
                    continue;
                }

                if (clusters.Contains(clusterId))
                {
                    return ep;
                }
            }
        }
        return 1;
    }

    /// <summary>Applies a thread-safe mutation to this device record.</summary>
    /// <param name="action">Mutation to apply while the device lock is held.</param>
    public void Mutate(Action<DeviceInfo> action)
    {
        lock (_lock)
        {
            action(this);
        }
    }
}
