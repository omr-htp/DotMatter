using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;

namespace DotMatter.Hosting.Devices;

/// <summary>
/// Shared command primitives for common Matter application clusters.
/// </summary>
public static class MatterDeviceCommands
{
    /// <summary>Common OnOff commands.</summary>
    public enum OnOffCommand
    {
        /// <summary>Turn on.</summary>
        On,
        /// <summary>Turn off.</summary>
        Off,
        /// <summary>Toggle current state.</summary>
        Toggle,
    }

    /// <summary>Sends an OnOff command.</summary>
    public static Task<InvokeResponse> SendOnOffAsync(
        ISession session,
        ushort endpointId,
        OnOffCommand command,
        CancellationToken ct = default)
        => command switch
        {
            OnOffCommand.On => new OnOffCluster(session, endpointId).OnAsync(ct),
            OnOffCommand.Off => new OnOffCluster(session, endpointId).OffAsync(ct),
            _ => new OnOffCluster(session, endpointId).ToggleAsync(ct)
        };

    /// <summary>Moves level and turns the device on when supported.</summary>
    public static Task<InvokeResponse> MoveToLevelWithOnOffAsync(
        ISession session,
        ushort endpointId,
        byte level,
        ushort transitionTime,
        LevelControlCluster.OptionsBitmap options = 0,
        CancellationToken ct = default)
        => new LevelControlCluster(session, endpointId)
            .MoveToLevelWithOnOffAsync(level, transitionTime, options, options, ct);

    /// <summary>Moves hue/saturation directly or converts to XY based on cached device capabilities.</summary>
    public static async Task<InvokeResponse> MoveToColorAsync(
        ISession session,
        ushort endpointId,
        DeviceInfo? device,
        byte hue,
        byte saturation,
        ushort transitionTime,
        ColorControlCluster.OptionsBitmap options = 0,
        CancellationToken ct = default)
    {
        var cluster = new ColorControlCluster(session, endpointId);
        if (device?.SupportsHueSaturation ?? true)
        {
            return await cluster.MoveToHueAndSaturationAsync(hue, saturation, transitionTime, options, options, ct);
        }

        var xy = ColorConversion.HueSatToXY(hue, saturation);
        return await cluster.MoveToColorAsync(xy.X, xy.Y, transitionTime, options, options, ct);
    }

    /// <summary>Moves directly to a CIE XY color.</summary>
    public static Task<InvokeResponse> MoveToColorXYAsync(
        ISession session,
        ushort endpointId,
        ushort x,
        ushort y,
        ushort transitionTime,
        ColorControlCluster.OptionsBitmap options = 0,
        CancellationToken ct = default)
        => new ColorControlCluster(session, endpointId).MoveToColorAsync(x, y, transitionTime, options, options, ct);

    /// <summary>Moves to a color temperature in mireds.</summary>
    public static Task<InvokeResponse> MoveToColorTemperatureAsync(
        ISession session,
        ushort endpointId,
        ushort mireds,
        ushort transitionTime,
        ColorControlCluster.OptionsBitmap options = 0,
        CancellationToken ct = default)
        => new ColorControlCluster(session, endpointId).MoveToColorTemperatureAsync(mireds, transitionTime, options, options, ct);
}
