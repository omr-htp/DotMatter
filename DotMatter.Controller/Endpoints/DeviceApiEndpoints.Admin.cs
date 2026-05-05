using DotMatter.Controller.Endpoints;
using DotMatter.Core;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceAdminEndpoints(RouteGroupBuilder devices)
    {
        devices.MapGet("/devices/{id}/commissioning", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceCommissioningStateQueryResult(
                await service.ReadCommissioningStateAsync(id));
        })
            .WithSummary("Read device commissioning state")
            .WithDescription("Reads Administrator Commissioning and General Commissioning state from endpoint 0 using the supported Core administration helpers.");

        devices.MapGet("/devices/{id}/fabrics", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceOperationalCredentialsQueryResult(
                await service.ReadOperationalCredentialsAsync(id));
        })
            .WithSummary("Read device operational credentials")
            .WithDescription("Reads Operational Credentials fabric inventory, NOCs, and trusted roots from endpoint 0 using the supported Core administration helpers.");

        devices.MapPost("/devices/{id}/commissioning/window/basic", async (string id, BasicCommissioningWindowRequest body, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.OpenBasicCommissioningWindowAsync(id, body.CommissioningTimeout),
                $"commissioning-window:{body.CommissioningTimeout}");
        })
            .WithSummary("Open a basic commissioning window")
            .WithDescription("Opens a basic commissioning window on endpoint 0 of a connected device.");

        devices.MapPost("/devices/{id}/commissioning/window/enhanced", async Task<IResult> (string id, EnhancedCommissioningWindowRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateEnhancedCommissioningWindowRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.PakePasscodeVerifierHex, out var pakePasscodeVerifier))
            {
                return Results.BadRequest(new ErrorResponse("PakePasscodeVerifierHex must be valid even-length hexadecimal"));
            }

            if (!TryParseRequiredHexBytes(body.SaltHex, out var salt))
            {
                return Results.BadRequest(new ErrorResponse("SaltHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.OpenCommissioningWindowAsync(
                    id,
                    new MatterEnhancedCommissioningWindowParameters(
                        body.CommissioningTimeout,
                        pakePasscodeVerifier,
                        body.Discriminator,
                        body.Iterations,
                        salt)),
                $"commissioning-window:enhanced:{body.CommissioningTimeout}");
        })
            .WithSummary("Open an enhanced commissioning window")
            .WithDescription("Opens an enhanced commissioning window on endpoint 0 of a connected device using explicit PAKE verifier, discriminator, iterations, and salt inputs.");

        devices.MapPost("/devices/{id}/commissioning/revoke", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.RevokeCommissioningAsync(id),
                "commissioning-window:revoked");
        })
            .WithSummary("Revoke the current commissioning window")
            .WithDescription("Revokes any open commissioning window on endpoint 0 of a connected device.");

        devices.MapPost("/devices/{id}/commissioning/complete", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.CompleteCommissioningAsync(id),
                "commissioning:complete");
        })
            .WithSummary("Send CommissioningComplete")
            .WithDescription("Sends CommissioningComplete to endpoint 0 of a connected device.");

        devices.MapPost("/devices/{id}/fabrics/label", async (string id, FabricLabelRequest body, MatterControllerService service) =>
        {
            if (string.IsNullOrWhiteSpace(body.Label))
            {
                return Results.BadRequest(new ErrorResponse("Label is required"));
            }

            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.UpdateFabricLabelAsync(id, body.Label),
                $"fabric-label:{body.Label}");
        })
            .WithSummary("Update the current fabric label")
            .WithDescription("Updates the current fabric label through Operational Credentials on endpoint 0 of a connected device.");

        devices.MapDelete("/devices/{id}/fabrics/{fabricIndex}", async (string id, byte fabricIndex, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            return missing ?? ApiEndpointResults.MapDeviceResult(
                await service.RemoveFabricAsync(id, fabricIndex),
                $"fabric-removed:{fabricIndex}");
        })
            .WithSummary("Remove a fabric")
            .WithDescription("Removes the selected fabric from a connected device through Operational Credentials on endpoint 0.");

        devices.MapDelete("/devices/{id}", async (string id, bool? localOnly, MatterControllerService service) =>
        {
            var result = await service.DeleteDeviceAsync(id, localOnly ?? false);
            if (result.Success)
            {
                return Results.Ok(new MessageResponse(
                    (localOnly ?? false)
                        ? $"Device {id} removed locally"
                        : $"Device {id} removed"));
            }

            return ApiEndpointResults.MapDeviceResult(result, "deleted");
        })
            .WithSummary("Delete a device")
            .WithDescription("By default removes the controller fabric from the node and then deletes the local registry/fabric state. Use localOnly=true to remove only local controller state.");
    }
}
