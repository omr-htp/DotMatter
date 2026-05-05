using DotMatter.Controller.Endpoints;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceControlEndpoints(RouteGroupBuilder devices)
    {
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
    }
}
