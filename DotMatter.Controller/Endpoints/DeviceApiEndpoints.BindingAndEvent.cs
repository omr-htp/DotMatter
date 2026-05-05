using DotMatter.Controller.Endpoints;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceBindingAndEventEndpoints(RouteGroupBuilder devices)
    {
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
    }
}
