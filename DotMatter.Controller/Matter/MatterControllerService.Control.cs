using DotMatter.Core.Clusters;
using DotMatter.Hosting.Devices;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
    /// <summary>Sends an On/Off command to a connected device.</summary>
    public async Task<DeviceOperationResult> SendCommandAsync(string id, DeviceCommand cmd)
    {
        if (!TryGetSessionOwner(id, out var rs))
        {
            Log.LogWarning("Device {Id} not connected", id);
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var endpointId = GetEndpoint(id, OnOffCluster.ClusterId);
        return await _commandExecutor.ExecuteAsync(
            id,
            $"command {cmd}",
            ct => rs.UseSessionAsync(session => cmd switch
            {
                DeviceCommand.On => MatterDeviceCommands.SendOnOffAsync(session, endpointId, MatterDeviceCommands.OnOffCommand.On, ct),
                DeviceCommand.Off => MatterDeviceCommands.SendOnOffAsync(session, endpointId, MatterDeviceCommands.OnOffCommand.Off, ct),
                _ => MatterDeviceCommands.SendOnOffAsync(session, endpointId, MatterDeviceCommands.OnOffCommand.Toggle, ct)
            }, ct),
            device => device.LastSeen = DateTime.UtcNow,
            cmd.ToString(),
            value => PublishEvent(id, "command", value),
            $"Device {id}: {cmd} OK",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets the level attribute for a connected device.</summary>
    public async Task<DeviceOperationResult> SetLevelAsync(string id, byte level, ushort transitionTime = 5)
    {
        if (!TryGetSessionOwner(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var endpointId = GetEndpoint(id, LevelControlCluster.ClusterId);
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetLevel",
            ct => rs.UseSessionAsync(session => MatterDeviceCommands.MoveToLevelWithOnOffAsync(
                session,
                endpointId,
                level,
                transitionTime,
                LevelControlCluster.OptionsBitmap.ExecuteIfOff,
                ct), ct),
            device =>
            {
                device.Level = level;
                device.LastSeen = DateTime.UtcNow;
            },
            $"level:{level}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Level -> {level}",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets hue and saturation for a connected device, converting to XY when needed.</summary>
    public async Task<DeviceOperationResult> SetColorAsync(string id, byte hue, byte saturation, ushort transitionTime = 5)
    {
        if (!TryGetSessionOwner(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var endpointId = GetEndpoint(id, ColorControlCluster.ClusterId);
        var device = Registry.Get(id);
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetColor",
            ct => rs.UseSessionAsync(async session =>
            {
                return await MatterDeviceCommands.MoveToColorAsync(
                    session,
                    endpointId,
                    device,
                    hue,
                    saturation,
                    transitionTime,
                    ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                    ct);
            }, ct),
            currentDevice =>
            {
                currentDevice.Hue = hue;
                currentDevice.Saturation = saturation;
                currentDevice.LastSeen = DateTime.UtcNow;
            },
            $"color:h{hue}s{saturation}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Color -> H:{hue} S:{saturation}",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>Sets the CIE XY color attributes for a connected device.</summary>
    public async Task<DeviceOperationResult> SetColorXYAsync(string id, ushort x, ushort y, ushort transitionTime = 5)
    {
        if (!TryGetSessionOwner(id, out var rs))
        {
            return new DeviceOperationResult(false, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var endpointId = GetEndpoint(id, ColorControlCluster.ClusterId);
        return await _commandExecutor.ExecuteAsync(
            id,
            "SetColorXY",
            ct => rs.UseSessionAsync(session => MatterDeviceCommands.MoveToColorXYAsync(
                session,
                endpointId,
                x,
                y,
                transitionTime,
                ColorControlCluster.OptionsBitmap.ExecuteIfOff,
                ct), ct),
            device =>
            {
                device.ColorX = x;
                device.ColorY = y;
                device.LastSeen = DateTime.UtcNow;
            },
            $"color-xy:x{x}y{y}",
            value => PublishEvent(id, "command", value),
            $"Device {id}: Color XY -> ({x}, {y})",
            () =>
            {
                TryScheduleManagedReconnect(id, rs);
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Reads and returns the latest known state for a connected device.
    /// </summary>
    public async Task<DeviceState?> ReadStateAsync(string id)
    {
        if (!Sessions.TryGetValue(id, out var rs) || rs.Session is null)
        {
            return null;
        }

        try
        {
            await ReadDeviceStateAsync(id, rs.Session);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Device {Id}: state read failed.", id);
        }

        var device = Registry.Get(id);
        return device == null ? null : new DeviceState(
            device.OnOff, device.IsOnline, device.LastSeen, device.Level, device.Hue, device.Saturation,
            device.ColorX, device.ColorY, device.ColorMode, device.VendorName, device.ProductName);
    }

    private ushort GetEndpoint(string deviceId, uint clusterId)
    {
        var device = Registry.Get(deviceId);
        return device?.EndpointFor(clusterId) ?? 1;
    }
}
