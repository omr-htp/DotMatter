using DotMatter.Core;
using System.Text.Json.Serialization.Metadata;

namespace DotMatter.Controller;

internal static class ApiEndpointResults
{
    internal static string? TryResolvePairingParameters(
        int discriminator,
        uint passcode,
        string? qrCode,
        string? manualCode,
        out int resolvedDiscriminator,
        out uint resolvedPasscode,
        out bool isShortDiscriminator)
    {
        resolvedDiscriminator = discriminator;
        resolvedPasscode = passcode > 0 ? passcode : 20202021;
        isShortDiscriminator = false;

        if (resolvedDiscriminator == 0 && !string.IsNullOrEmpty(qrCode))
        {
            var parsed = PairingCodeParser.ParseQrCode(qrCode);
            if (parsed == null)
            {
                return "Invalid QR code";
            }

            (resolvedDiscriminator, resolvedPasscode, isShortDiscriminator) = parsed.Value;
        }
        else if (resolvedDiscriminator == 0 && !string.IsNullOrEmpty(manualCode))
        {
            var parsed = PairingCodeParser.ParseManualCode(manualCode);
            if (parsed == null)
            {
                return "Invalid manual pairing code";
            }

            (resolvedDiscriminator, resolvedPasscode, isShortDiscriminator) = parsed.Value;
        }

        return resolvedDiscriminator == 0 ? "Provide discriminator, qrCode, or manualCode" : null;
    }

    internal static IResult MapDeviceResult(DeviceOperationResult result, string successValue)
        => result.Success
            ? Results.Ok(new CommandResult(successValue))
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Operation timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Device command failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceBindingQueryResult(MatterControllerService.DeviceBindingQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Binding read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Binding read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceAclQueryResult(MatterControllerService.DeviceAclQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "ACL read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "ACL read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceBindingRemovalResult(MatterControllerService.DeviceBindingRemovalResult result)
        => MapRemovalResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceBindingRemovalResponse,
            "Binding removal failed");

    internal static IResult MapDeviceAclRemovalResult(MatterControllerService.DeviceAclRemovalResult result)
        => MapRemovalResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceAclRemovalResponse,
            "ACL removal failed");

    internal static IResult MapSwitchBindingRemovalResult(MatterControllerService.SwitchBindingRemovalResult result)
        => MapRemovalResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.SwitchBindingRemovalResponse,
            "Switch route removal failed");

    internal static IResult MapFabricBindingQueryResult(MatterControllerService.FabricBindingQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Fabric not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Binding query timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Binding query failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapFabricAclQueryResult(MatterControllerService.FabricAclQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Fabric not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "ACL query timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "ACL query failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapCommissionFailure(ControllerCommissioningResult result)
        => result.Error?.Contains("already in progress", StringComparison.OrdinalIgnoreCase) == true
            ? Results.Conflict(result)
            : Results.UnprocessableEntity(result);

    internal static IResult MapWifiCommissionFailure(WifiCommissioningResult result)
        => result.Error?.Contains("already in progress", StringComparison.OrdinalIgnoreCase) == true
            ? Results.Conflict(result)
            : Results.UnprocessableEntity(result);

    private static IResult MapRemovalResult<TResponse>(
        bool success,
        DeviceOperationFailure failure,
        TResponse? response,
        string? error,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        string defaultError)
        where TResponse : class
    {
        if (success && response is not null)
        {
            return Results.Ok(response);
        }

        var statusCode = failure switch
        {
            DeviceOperationFailure.NotFound => StatusCodes.Status404NotFound,
            DeviceOperationFailure.Timeout => StatusCodes.Status503ServiceUnavailable,
            DeviceOperationFailure.NotConnected => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway,
        };

        return response is not null
            ? Results.Json(response, jsonTypeInfo, statusCode: statusCode)
            : Results.Json(
                new ErrorResponse(error ?? defaultError),
                ControllerJsonContext.Default.ErrorResponse,
                statusCode: statusCode);
    }
}
