using DotMatter.Controller.Endpoints;
using DotMatter.Hosting.Devices;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceOverviewEndpoints(RouteGroupBuilder devices)
    {
        devices.MapGet("/acls", async (HttpContext context, MatterControllerService service) =>
            ApiEndpointResults.MapFabricAclQueryResult(
                await service.ReadFabricAclsAsync(GetLegacyFabricName(context))))
            .WithSummary("List fabric ACLs")
            .WithDescription("Reads AccessControl ACL entries from known devices on the configured shared fabric. Returns per-device errors when a target device is offline or cannot be read.");

        devices.MapGet("/bindings", async (HttpContext context, ushort? endpoint, MatterControllerService service) =>
        {
            if (endpoint == 0)
            {
                return Results.BadRequest(new ErrorResponse("Endpoint must be greater than 0"));
            }

            return ApiEndpointResults.MapFabricBindingQueryResult(
                await service.ReadFabricBindingsAsync(GetLegacyFabricName(context), endpoint));
        })
            .WithSummary("List fabric bindings")
            .WithDescription("Reads Binding cluster entries from known devices on the configured shared fabric. Returns per-device errors when a source device is offline or cannot be read.");

        devices.MapGet("/devices", (DeviceRegistry registry) =>
            registry.GetAll().Select(device => new DeviceSummary(
                device.Id, device.Name, device.NodeId, device.Ip, device.Port,
                device.IsOnline, device.OnOff, device.Level, device.Hue, device.Saturation, device.LastSeen, device.DeviceType)))
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

        devices.MapGet("/devices/{id}/capabilities", async (string id, MatterControllerService service, bool refresh = false) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceCapabilityQueryResult(
                await service.ReadCapabilitiesAsync(id, refresh));
        })
            .WithSummary("Get device capability snapshot")
            .WithDescription("Returns controller-observed device topology and derived capability flags for UI rendering.");
    }
}
