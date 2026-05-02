using DotMatter.Core.Clusters;
using DotMatter.Hosting;
using System.Globalization;

namespace DotMatter.Controller;

internal static class DeviceApiEndpoints
{
    internal static void MapDeviceApiEndpoints(this RouteGroupBuilder api)
    {
        var devices = api.MapGroup(string.Empty).WithTags("Devices");

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

        devices.MapGet("/devices/{id}/acl", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceAclQueryResult(
                await service.ReadAclAsync(id));
        })
            .WithSummary("List device ACL")
            .WithDescription("Reads AccessControl ACL entries from endpoint 0 of one device.");

        devices.MapGet("/devices/{id}/bindings", async (string id, ushort? endpoint, MatterControllerService service) =>
        {
            if (endpoint == 0)
            {
                return Results.BadRequest(new ErrorResponse("Endpoint must be greater than 0"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceBindingQueryResult(
                await service.ReadBindingsAsync(id, endpoint ?? 1));
        })
            .WithSummary("List device bindings")
            .WithDescription("Reads Binding cluster entries from one source device endpoint. The endpoint query parameter defaults to 1.");

        devices.MapGet("/devices/{id}/matter/events", async (
            string id,
            string? cluster,
            string? eventId,
            ushort? endpoint,
            bool? fabricFiltered,
            MatterControllerService service) =>
        {
            if (string.IsNullOrWhiteSpace(cluster))
            {
                return Results.BadRequest(new ErrorResponse("Cluster is required"));
            }

            if (!TryParseHexOrDecimalUInt(cluster, out var clusterId))
            {
                return Results.BadRequest(new ErrorResponse("Cluster must be an unsigned integer in decimal or 0x-prefixed hex"));
            }

            if (!TryParseOptionalHexOrDecimalUInt(eventId, out var parsedEventId))
            {
                return Results.BadRequest(new ErrorResponse("eventId must be an unsigned integer in decimal or 0x-prefixed hex"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceMatterEventQueryResult(
                await service.ReadMatterEventsAsync(id, endpoint, clusterId, parsedEventId, fabricFiltered ?? false));
        })
            .WithSummary("Read device Matter events")
            .WithDescription("Reads raw Matter event envelopes from one device for the requested cluster and optional event ID. This testing-focused API exposes live protocol events without reusing the controller's internal SSE stream.");

        devices.MapPost("/devices/{id}/bindings/remove", async (string id, DeviceBindingRemovalRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateBindingRemovalRequest(body);
            if (validationError is not null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceBindingRemovalResult(
                await service.RemoveBindingEntriesAsync(id, body));
        })
            .WithSummary("Remove binding entries")
            .WithDescription("Removes matching Binding cluster entries from one source device endpoint using explicit request-body match criteria.");

        devices.MapPost("/devices/{id}/acl/remove", async (string id, DeviceAclRemovalRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateAclRemovalRequest(body);
            if (validationError is not null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceAclRemovalResult(
                await service.RemoveAclEntriesAsync(id, body));
        })
            .WithSummary("Remove ACL entries")
            .WithDescription("Removes matching AccessControl ACL entries from endpoint 0 of one device using explicit request-body match criteria.");

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
            var validationError = ValidateSwitchBindingRequest(body);
            if (validationError is not null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

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

        devices.MapPost("/devices/{id}/bindings/onoff/remove", async (string id, SwitchBindingRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateSwitchBindingRequest(body);
            if (validationError is not null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            if (!service.HasDevice(body.TargetDeviceId))
            {
                return Results.NotFound(new ErrorResponse($"Target device {body.TargetDeviceId} not found"));
            }

            return ApiEndpointResults.MapSwitchBindingRemovalResult(
                await service.UnbindSwitchOnOffAsync(id, body.TargetDeviceId, body.SourceEndpoint, body.TargetEndpoint));
        })
            .WithSummary("Unbind switch from OnOff target")
            .WithDescription("Removes the matching switch Binding entry and conservatively reconciles the matching target ACL grant for the OnOff route.");

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

    private static string? GetLegacyFabricName(HttpContext context)
    {
        var fabricName = context.Request.Query["fabricName"].ToString();
        return string.IsNullOrWhiteSpace(fabricName) ? null : fabricName;
    }

    private static bool TryParseOptionalHexOrDecimalUInt(string? value, out uint? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseHexOrDecimalUInt(value, out var parsedValue))
        {
            return false;
        }

        parsed = parsedValue;
        return true;
    }

    private static bool TryParseHexOrDecimalUInt(string value, out uint parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static string? ValidateSwitchBindingRequest(SwitchBindingRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.TargetDeviceId))
        {
            return "TargetDeviceId is required";
        }

        return body.SourceEndpoint == 0 || body.TargetEndpoint == 0
            ? "SourceEndpoint and TargetEndpoint must be greater than 0"
            : null;
    }

    private static string? ValidateBindingRemovalRequest(DeviceBindingRemovalRequest body)
    {
        if (body.Endpoint == 0)
        {
            return "Endpoint must be greater than 0";
        }

        if (!string.IsNullOrWhiteSpace(body.NodeId) && !ulong.TryParse(body.NodeId, out _))
        {
            return "NodeId must be an unsigned integer";
        }

        return string.IsNullOrWhiteSpace(body.NodeId)
               && body.Group is null
               && body.TargetEndpoint is null
               && body.Cluster is null
            ? "Provide at least one Binding match field"
            : null;
    }

    private static string? ValidateAclRemovalRequest(DeviceAclRemovalRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Privilege) || string.IsNullOrWhiteSpace(body.AuthMode))
        {
            return "Privilege and AuthMode are required";
        }

        if (!Enum.TryParse<AccessControlCluster.AccessControlEntryPrivilegeEnum>(body.Privilege, ignoreCase: true, out _))
        {
            return $"Unknown ACL privilege '{body.Privilege}'";
        }

        if (!Enum.TryParse<AccessControlCluster.AccessControlEntryAuthModeEnum>(body.AuthMode, ignoreCase: true, out _))
        {
            return $"Unknown ACL auth mode '{body.AuthMode}'";
        }

        if (!string.IsNullOrWhiteSpace(body.AuxiliaryType)
            && !Enum.TryParse<AccessControlCluster.AccessControlAuxiliaryTypeEnum>(body.AuxiliaryType, ignoreCase: true, out _))
        {
            return $"Unknown ACL auxiliary type '{body.AuxiliaryType}'";
        }

        if (body.Subjects is not null && body.Subjects.Any(subject => !ulong.TryParse(subject, out _)))
        {
            return "ACL subjects must be unsigned integers";
        }

        return body.Subjects is null
               && body.Targets is null
               && string.IsNullOrWhiteSpace(body.AuxiliaryType)
            ? "Provide subjects, targets, or AuxiliaryType to match ACL entries"
            : null;
    }
}
