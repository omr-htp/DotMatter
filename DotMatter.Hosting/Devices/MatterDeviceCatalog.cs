using DotMatter.Core.Clusters;

namespace DotMatter.Hosting.Devices;

/// <summary>
/// Shared Matter device-type and cluster labels used by host consumers.
/// </summary>
public static class MatterDeviceCatalog
{
    /// <summary>Maps a Matter device type id to a controller-facing type label.</summary>
    public static string? GetDeviceTypeLabel(uint deviceType)
        => deviceType switch
        {
            0x0000000F => "switch",
            0x00000100 => "on_off_light",
            0x00000101 => "dimmable_light",
            0x0000010A => "on_off_plug",
            0x0000010C => "color_temperature_light",
            0x0000010D => "color_light",
            _ => null
        };

    /// <summary>Maps a Matter device type id to a SmartHome peripheral type label.</summary>
    public static string? GetSmartHomePeripheralType(uint deviceType)
        => GetDeviceTypeLabel(deviceType) switch
        {
            "switch" => "light_switch",
            "color_light" => "extended_color_light",
            { } label => label,
            null => null
        };

    /// <summary>Returns a user-facing peripheral name for a type label.</summary>
    public static string GetPeripheralName(string peripheralType)
        => peripheralType switch
        {
            "switch" or "light_switch" => "Light Switch",
            "on_off_light" => "Light",
            "dimmable_light" => "Dimmable Light",
            "on_off_plug" => "Smart Plug",
            "color_temperature_light" => "Color Temperature Light",
            "color_light" or "extended_color_light" => "Color Light",
            _ => "Matter Device"
        };

    /// <summary>Returns a human-readable cluster name for known generated clusters.</summary>
    public static string? GetClusterName(uint clusterId)
        => clusterId switch
        {
            AccessControlCluster.ClusterId => "Access Control",
            AdministratorCommissioningCluster.ClusterId => "Administrator Commissioning",
            BasicInformationCluster.ClusterId => "Basic Information",
            BindingCluster.ClusterId => "Binding",
            ColorControlCluster.ClusterId => "Color Control",
            DescriptorCluster.ClusterId => "Descriptor",
            GeneralCommissioningCluster.ClusterId => "General Commissioning",
            GroupKeyManagementCluster.ClusterId => "Group Key Management",
            GroupsCluster.ClusterId => "Groups",
            LevelControlCluster.ClusterId => "Level Control",
            NetworkCommissioningCluster.ClusterId => "Network Commissioning",
            OnOffCluster.ClusterId => "On/Off",
            OperationalCredentialsCluster.ClusterId => "Operational Credentials",
            ScenesManagementCluster.ClusterId => "Scenes Management",
            SwitchCluster.ClusterId => "Switch",
            _ => ClusterEventRegistry.GetClusterName(clusterId)
        };
}
