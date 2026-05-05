using System.Text;
using DotMatter.Controller.Endpoints;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceNetworkCommissioningEndpoints(RouteGroupBuilder devices)
    {
        devices.MapGet("/devices/{id}/network-commissioning", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningQueryResult(
                await service.ReadNetworkCommissioningStateAsync(id));
        })
            .WithSummary("Read device network commissioning state")
            .WithDescription("Reads Network Commissioning state from endpoint 0 using the supported Core administration helpers, including stored networks, interface state, feature flags, and last network status.");

        devices.MapPost("/devices/{id}/network-commissioning/scan", async Task<IResult> (string id, NetworkCommissioningScanRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningScanRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            var ssidBytes = body.Ssid is null ? null : Encoding.UTF8.GetBytes(body.Ssid);
            return ApiEndpointResults.MapDeviceNetworkCommissioningScanResult(
                await service.ScanNetworksAsync(id, ssidBytes, body.Breadcrumb));
        })
            .WithSummary("Scan visible networks")
            .WithDescription("Runs the Network Commissioning ScanNetworks command on endpoint 0. Optionally filters Wi-Fi scans by a UTF-8 SSID.");

        devices.MapPost("/devices/{id}/network-commissioning/wifi", async Task<IResult> (string id, NetworkCommissioningWiFiRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningWiFiRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseOptionalHexBytes(body.NetworkIdentityHex, out var networkIdentity))
            {
                return Results.BadRequest(new ErrorResponse("NetworkIdentityHex must be valid even-length hexadecimal"));
            }

            if (!TryParseOptionalHexBytes(body.ClientIdentifierHex, out var clientIdentifier))
            {
                return Results.BadRequest(new ErrorResponse("ClientIdentifierHex must be valid even-length hexadecimal"));
            }

            if (!TryParseOptionalHexBytes(body.PossessionNonceHex, out var possessionNonce))
            {
                return Results.BadRequest(new ErrorResponse("PossessionNonceHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningCommandResult(
                await service.AddOrUpdateWiFiNetworkAsync(
                    id,
                    Encoding.UTF8.GetBytes(body.Ssid),
                    Encoding.UTF8.GetBytes(body.Credentials ?? string.Empty),
                    body.Breadcrumb,
                    networkIdentity,
                    clientIdentifier,
                    possessionNonce));
        })
            .WithSummary("Add or update a Wi-Fi network")
            .WithDescription("Runs AddOrUpdateWiFiNetwork on endpoint 0 using a UTF-8 SSID and credentials, with optional binary identity fields passed as hexadecimal.");

        devices.MapPost("/devices/{id}/network-commissioning/thread", async Task<IResult> (string id, NetworkCommissioningThreadRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningThreadRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.OperationalDatasetHex, out var dataset))
            {
                return Results.BadRequest(new ErrorResponse("OperationalDatasetHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningCommandResult(
                await service.AddOrUpdateThreadNetworkAsync(id, dataset, body.Breadcrumb));
        })
            .WithSummary("Add or update a Thread network")
            .WithDescription("Runs AddOrUpdateThreadNetwork on endpoint 0 using a hexadecimal operational dataset.");

        devices.MapPost("/devices/{id}/network-commissioning/connect", async Task<IResult> (string id, NetworkCommissioningNetworkIdRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningNetworkIdRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.NetworkIdHex, out var networkId))
            {
                return Results.BadRequest(new ErrorResponse("NetworkIdHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningConnectResult(
                await service.ConnectNetworkAsync(id, networkId, body.Breadcrumb));
        })
            .WithSummary("Connect to one configured network")
            .WithDescription("Runs ConnectNetwork on endpoint 0 using a hexadecimal network identifier.");

        devices.MapPost("/devices/{id}/network-commissioning/remove", async Task<IResult> (string id, NetworkCommissioningNetworkIdRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningNetworkIdRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.NetworkIdHex, out var networkId))
            {
                return Results.BadRequest(new ErrorResponse("NetworkIdHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningCommandResult(
                await service.RemoveNetworkAsync(id, networkId, body.Breadcrumb));
        })
            .WithSummary("Remove one configured network")
            .WithDescription("Runs RemoveNetwork on endpoint 0 using a hexadecimal network identifier.");

        devices.MapPost("/devices/{id}/network-commissioning/reorder", async Task<IResult> (string id, NetworkCommissioningReorderRequest body, MatterControllerService service) =>
        {
            var validationError = ValidateNetworkCommissioningReorderRequest(body);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.NetworkIdHex, out var networkId))
            {
                return Results.BadRequest(new ErrorResponse("NetworkIdHex must be valid even-length hexadecimal"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkCommissioningCommandResult(
                await service.ReorderNetworkAsync(id, networkId, body.NetworkIndex, body.Breadcrumb));
        })
            .WithSummary("Reorder one configured network")
            .WithDescription("Runs ReorderNetwork on endpoint 0 using a hexadecimal network identifier and a target index.");

        devices.MapPost("/devices/{id}/network-commissioning/interface-enabled", async (string id, NetworkCommissioningInterfaceEnabledRequest body, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceNetworkInterfaceWriteResult(
                await service.WriteNetworkInterfaceEnabledAsync(id, body.InterfaceEnabled));
        })
            .WithSummary("Enable or disable the network interface")
            .WithDescription("Writes the InterfaceEnabled attribute on endpoint 0 of the Network Commissioning cluster.");
    }

    private static bool TryParseOptionalHexBytes(string? value, out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseRequiredHexBytes(value, out var parsed))
        {
            return false;
        }

        bytes = parsed;
        return true;
    }
}
