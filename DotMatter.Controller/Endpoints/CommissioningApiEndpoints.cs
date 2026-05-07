using DotMatter.Controller.Matter;

namespace DotMatter.Controller.Endpoints;

internal static class CommissioningApiEndpoints
{
    internal static void MapCommissioningApiEndpoints(this RouteGroupBuilder api)
    {
        var commissioning = api.MapGroup(string.Empty).WithTags("Commissioning");

        commissioning.MapPost("/commission", async (
            CommissionRequest body,
            CommissioningService commissioningService,
            MatterControllerService controllerService,
            CancellationToken ct) =>
        {
            var error = ApiEndpointResults.TryResolvePairingParameters(
                body.Discriminator,
                body.Passcode,
                body.QrCode,
                body.ManualCode,
                out var discriminator,
                out var passcode,
                out var isShortDiscriminator);
            if (error != null)
            {
                return Results.BadRequest(new ControllerCommissioningResult(false, null, null, null, error));
            }

            var result = await commissioningService.CommissionDeviceAsync(
                discriminator,
                passcode,
                body.FabricName ?? string.Empty,
                ct,
                isShortDiscriminator,
                provisionThreadNetwork: !body.SkipNetworkProvisioning);
            if (!result.Success)
            {
                return ApiEndpointResults.MapCommissionFailure(result);
            }

            if (result.DeviceId is not null)
            {
                controllerService.TryScheduleConnectNewDevice(result.DeviceId);
            }

            return Results.Ok(result);
        })
            .WithSummary("Commission a new device")
            .WithDescription("Starts BLE commissioning. This takes 30-60 seconds. Use a client with long timeout.");

        commissioning.MapPost("/commission/wifi", async (
            WifiCommissionRequest body,
            CommissioningService commissioningService,
            MatterControllerService controllerService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.WifiSsid))
            {
                return Results.BadRequest(new WifiCommissioningResult(false, null, null, null, "WifiSsid is required"));
            }

            var error = ApiEndpointResults.TryResolvePairingParameters(
                body.Discriminator,
                body.Passcode,
                body.QrCode,
                body.ManualCode,
                out var discriminator,
                out var passcode,
                out var isShortDiscriminator);
            if (error != null)
            {
                return Results.BadRequest(new WifiCommissioningResult(false, null, null, null, error));
            }

            var result = await commissioningService.CommissionWifiDeviceAsync(
                discriminator,
                passcode,
                body.FabricName ?? string.Empty,
                body.WifiSsid,
                body.WifiPassword,
                ct,
                isShortDiscriminator);

            if (!result.Success)
            {
                return ApiEndpointResults.MapWifiCommissionFailure(result);
            }

            if (result.DeviceId is not null)
            {
                controllerService.TryScheduleConnectNewDevice(result.DeviceId);
            }

            return Results.Ok(result);
        })
            .WithSummary("Commission a WiFi device")
            .WithDescription("Commissions a WiFi Matter device via BLE. Provide WiFi SSID and password for network provisioning.");
    }
}
