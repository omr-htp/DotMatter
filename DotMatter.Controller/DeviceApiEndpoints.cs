using DotMatter.Hosting;

namespace DotMatter.Controller;

internal static class DeviceApiEndpoints
{
    internal static void MapDeviceApiEndpoints(this RouteGroupBuilder api)
    {
        var devices = api.MapGroup(string.Empty).WithTags("Devices");

        devices.MapGet("/devices", (DeviceRegistry registry) =>
            registry.GetAll().Select(device => new DeviceSummary(
                device.Id, device.Name, device.NodeId, device.Ip, device.Port,
                device.IsOnline, device.OnOff, device.Level, device.Hue, device.Saturation, device.LastSeen)))
            .WithSummary("List all devices")
            .WithDescription("Returns all commissioned Matter devices and their current state.");

        devices.MapGet("/devices/{id}", (string id, DeviceRegistry registry) =>
        {
            var device = registry.Get(id);
            return device is null
                ? Results.NotFound()
                : Results.Ok(new DeviceDetail(
                    device.Id, device.Name, device.NodeId, device.Ip, device.Port,
                    device.IsOnline, device.OnOff, device.Level, device.Hue, device.Saturation, device.ColorMode,
                    device.LastSeen, device.VendorName, device.ProductName));
        })
            .WithSummary("Get device details")
            .WithDescription("Returns detailed info for a specific device by ID.");

        devices.MapPost("/devices/{id}/on", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(await service.SendCommandAsync(id, DeviceCommand.On), "on");
        })
            .WithSummary("Turn device on")
            .WithDescription("Sends On command to the device OnOff cluster.");

        devices.MapPost("/devices/{id}/off", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(await service.SendCommandAsync(id, DeviceCommand.Off), "off");
        })
            .WithSummary("Turn device off")
            .WithDescription("Sends Off command to the device OnOff cluster.");

        devices.MapPost("/devices/{id}/toggle", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(await service.SendCommandAsync(id, DeviceCommand.Toggle), "toggled");
        })
            .WithSummary("Toggle device")
            .WithDescription("Sends Toggle command to the device OnOff cluster.");

        devices.MapGet("/devices/{id}/state", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            var state = await service.ReadStateAsync(id);
            return state is null
                ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(state);
        })
            .WithSummary("Read device state")
            .WithDescription("Reads current OnOff, Level, and Color state from the device.");

        devices.MapPost("/devices/{id}/level", async (string id, LevelRequest body, MatterControllerService service) =>
        {
            if (body.Level > 254)
            {
                return Results.BadRequest(new ErrorResponse("Level must be 0-254"));
            }

            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.SetLevelAsync(id, body.Level, body.TransitionTime),
                $"level:{body.Level}");
        })
            .WithSummary("Set brightness level")
            .WithDescription("Sets the device brightness level (0-254). TransitionTime in tenths of a second.");

        devices.MapPost("/devices/{id}/color", async (string id, ColorRequest body, MatterControllerService service) =>
        {
            if (body.Hue > 254 || body.Saturation > 254)
            {
                return Results.BadRequest(new ErrorResponse("Hue and Saturation must be 0-254"));
            }

            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.SetColorAsync(id, body.Hue, body.Saturation, body.TransitionTime),
                $"color:h{body.Hue}s{body.Saturation}");
        })
            .WithSummary("Set color (Hue/Saturation)")
            .WithDescription("Sets device color using Matter hue (0-254) and saturation (0-254).");

        devices.MapPost("/devices/{id}/color-xy", async (string id, ColorXYRequest body, MatterControllerService service) =>
        {
            if (body.X > 65279 || body.Y > 65279)
            {
                return Results.BadRequest(new ErrorResponse("X and Y must be 0-65279"));
            }

            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.SetColorXYAsync(id, body.X, body.Y, body.TransitionTime),
                $"color-xy:x{body.X}y{body.Y}");
        })
            .WithSummary("Set color (CIE XY)")
            .WithDescription("Sets device color using CIE 1931 XY coordinates (0-65279).");

        devices.MapPost("/devices/{id}/bindings/onoff", async (string id, SwitchBindingRequest body, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            if (!service.HasDevice(body.TargetDeviceId))
            {
                return Results.NotFound(new ErrorResponse($"Target device {body.TargetDeviceId} not found"));
            }

            return ApiEndpointResults.MapDeviceResult(
                await service.BindSwitchOnOffAsync(id, body.TargetDeviceId, body.SourceEndpoint, body.TargetEndpoint),
                $"bound:{body.TargetDeviceId}");
        })
            .WithSummary("Bind switch to OnOff target")
            .WithDescription("Writes target ACL and switch Binding entries so switch button events operate the target OnOff endpoint.");

        devices.MapDelete("/devices/{id}", (string id, MatterControllerService service) =>
        {
            var removed = service.DisconnectAndRemove(id);
            return removed
                ? Results.Ok(new MessageResponse($"Device {id} removed"))
                : Results.NotFound(new MessageResponse($"Device {id} not found"));
        })
            .WithSummary("Delete a device")
            .WithDescription("Disconnects and removes a commissioned device, deleting its fabric data from disk.");
    }

    private static IResult? EnsureKnownDevice(string id, MatterControllerService service)
        => service.HasDevice(id)
            ? null
            : Results.NotFound(new ErrorResponse($"Device {id} not found"));
}
