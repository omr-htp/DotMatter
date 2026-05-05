using System.Text.Json.Serialization.Metadata;
using DotMatter.Controller.Matter;
using DotMatter.Core;

namespace DotMatter.Controller.Endpoints;

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

            resolvedDiscriminator = parsed.Discriminator;
            resolvedPasscode = parsed.Passcode;
            isShortDiscriminator = parsed.IsShort;
        }
        else if (resolvedDiscriminator == 0 && !string.IsNullOrEmpty(manualCode))
        {
            var parsed = PairingCodeParser.ParseManualCode(manualCode);
            if (parsed == null)
            {
                return "Invalid manual pairing code";
            }

            resolvedDiscriminator = parsed.Discriminator;
            resolvedPasscode = parsed.Passcode;
            isShortDiscriminator = parsed.IsShort;
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
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
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
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
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

    internal static IResult MapDeviceMatterEventQueryResult(MatterControllerService.DeviceMatterEventQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Matter event read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Matter event read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceCapabilityQueryResult(MatterControllerService.DeviceCapabilitySnapshotQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Device capability discovery timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Device capability discovery failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceCommissioningStateQueryResult(MatterControllerService.DeviceCommissioningStateQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Commissioning state read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Commissioning state read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceOperationalCredentialsQueryResult(MatterControllerService.DeviceOperationalCredentialsQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Operational credentials read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Operational credentials read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceNetworkCommissioningQueryResult(MatterControllerService.DeviceNetworkCommissioningQueryResult result)
        => result.Success && result.Response is not null
            ? Results.Ok(result.Response)
            : result.Failure switch
            {
                DeviceOperationFailure.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Device not found")),
                DeviceOperationFailure.Timeout => Results.Json(
                    new ErrorResponse(result.Error ?? "Network commissioning read timed out"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                DeviceOperationFailure.Unsupported => Results.Json(
                    new ErrorResponse(result.Error ?? "Requested endpoint or cluster is not supported"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status409Conflict),
                DeviceOperationFailure.NotConnected => Results.Json(
                    new ErrorResponse(result.Error ?? "Device is not connected"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(
                    new ErrorResponse(result.Error ?? "Network commissioning read failed"),
                    ControllerJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway)
            };

    internal static IResult MapDeviceNetworkCommissioningScanResult(MatterControllerService.DeviceNetworkCommissioningScanResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceNetworkCommissioningScanResponse,
            "Network scan failed");

    internal static IResult MapDeviceNetworkCommissioningCommandResult(MatterControllerService.DeviceNetworkCommissioningCommandResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceNetworkCommissioningCommandResponse,
            "Network command failed");

    internal static IResult MapDeviceNetworkCommissioningConnectResult(MatterControllerService.DeviceNetworkCommissioningConnectResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceNetworkCommissioningConnectResponse,
            "Network connect failed");

    internal static IResult MapDeviceNetworkInterfaceWriteResult(MatterControllerService.DeviceNetworkInterfaceWriteResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceNetworkInterfaceWriteResponse,
            "Network interface write failed");

    internal static IResult MapDeviceGroupsQueryResult(MatterControllerService.DeviceGroupsQueryResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupsStateResponse,
            "Groups read failed");

    internal static IResult MapDeviceGroupKeyManagementQueryResult(MatterControllerService.DeviceGroupKeyManagementQueryResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupKeyManagementStateResponse,
            "Group key management read failed");

    internal static IResult MapDeviceScenesQueryResult(MatterControllerService.DeviceScenesQueryResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceScenesStateResponse,
            "Scenes read failed");

    internal static IResult MapDeviceGroupCommandResult(MatterControllerService.DeviceGroupCommandResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupCommandResponse,
            "Group command failed");

    internal static IResult MapDeviceGroupMembershipResult(MatterControllerService.DeviceGroupMembershipResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupMembershipResponse,
            "Group membership query failed");

    internal static IResult MapDeviceInvokeOnlyCommandResult(MatterControllerService.DeviceInvokeOnlyCommandResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceInvokeOnlyCommandResponse,
            "Device command failed");

    internal static IResult MapDeviceGroupKeySetReadResult(MatterControllerService.DeviceGroupKeySetReadResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupKeySetReadResponse,
            "Group key read failed");

    internal static IResult MapDeviceGroupKeySetIndicesResult(MatterControllerService.DeviceGroupKeySetIndicesResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceGroupKeySetIndicesResponse,
            "Group key index read failed");

    internal static IResult MapDeviceSceneCommandResult(MatterControllerService.DeviceSceneCommandResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceSceneCommandResponse,
            "Scene command failed");

    internal static IResult MapDeviceSceneViewResult(MatterControllerService.DeviceSceneViewResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceSceneViewResponse,
            "Scene read failed");

    internal static IResult MapDeviceSceneMembershipResult(MatterControllerService.DeviceSceneMembershipResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceSceneMembershipResponse,
            "Scene membership query failed");

    internal static IResult MapDeviceSceneCopyResult(MatterControllerService.DeviceSceneCopyResult result)
        => MapTypedResponseResult(
            result.Success,
            result.Failure,
            result.Response,
            result.Error,
            ControllerJsonContext.Default.DeviceSceneCopyResponse,
            "Scene copy failed");

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
            DeviceOperationFailure.Unsupported => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway,
        };

        return response is not null
            ? Results.Json(response, jsonTypeInfo, statusCode: statusCode)
            : Results.Json(
                new ErrorResponse(error ?? defaultError),
                ControllerJsonContext.Default.ErrorResponse,
                statusCode: statusCode);
    }

    private static IResult MapTypedResponseResult<TResponse>(
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
            DeviceOperationFailure.Unsupported => StatusCodes.Status409Conflict,
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
