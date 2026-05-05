using DotMatter.Controller.Endpoints;
using DotMatter.Core;
using DotMatter.Core.Clusters;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    private static void MapDeviceGroupManagementEndpoints(RouteGroupBuilder devices)
    {
        devices.MapGet("/devices/{id}/groups", async Task<IResult> (string id, ushort endpoint, MatterControllerService service) =>
        {
            if (ValidateEndpoint(endpoint) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupsQueryResult(await service.ReadGroupsStateAsync(id, endpoint));
        })
            .WithSummary("Read endpoint groups state")
            .WithDescription("Reads the promoted Groups state for one application endpoint, including NameSupport and the current endpoint membership projected from Group Key Management state.");

        devices.MapGet("/devices/{id}/group-keys", async (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupKeyManagementQueryResult(await service.ReadGroupKeyManagementStateAsync(id));
        })
            .WithSummary("Read group key management state")
            .WithDescription("Reads the promoted Group Key Management state from endpoint 0, including group-key maps, group tables, and per-fabric limits.");

        devices.MapGet("/devices/{id}/scenes", async Task<IResult> (string id, ushort endpoint, MatterControllerService service) =>
        {
            if (ValidateEndpoint(endpoint) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceScenesQueryResult(await service.ReadScenesStateAsync(id, endpoint));
        })
            .WithSummary("Read endpoint scenes state")
            .WithDescription("Reads the promoted Scenes Management state for one application endpoint, including SceneTableSize and the fabric-scoped scene info attribute.");

        devices.MapPost("/devices/{id}/groups/add", async Task<IResult> (string id, GroupAddRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupAddRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupCommandResult(await service.AddGroupAsync(id, body.Endpoint, body.GroupId, body.GroupName));
        })
            .WithSummary("Add one group")
            .WithDescription("Runs AddGroup on one application endpoint using the promoted Groups controller workflow.");

        devices.MapPost("/devices/{id}/groups/view", async Task<IResult> (string id, GroupCommandRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupCommandRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupCommandResult(await service.ViewGroupAsync(id, body.Endpoint, body.GroupId));
        })
            .WithSummary("View one group")
            .WithDescription("Runs ViewGroup on one application endpoint.");

        devices.MapPost("/devices/{id}/groups/membership", async Task<IResult> (string id, GroupMembershipRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupMembershipRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupMembershipResult(await service.GetGroupMembershipAsync(id, body.Endpoint, body.GroupIds));
        })
            .WithSummary("Read group membership")
            .WithDescription("Runs GetGroupMembership on one application endpoint.");

        devices.MapPost("/devices/{id}/groups/remove", async Task<IResult> (string id, GroupCommandRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupCommandRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupCommandResult(await service.RemoveGroupAsync(id, body.Endpoint, body.GroupId));
        })
            .WithSummary("Remove one group")
            .WithDescription("Runs RemoveGroup on one application endpoint.");

        devices.MapPost("/devices/{id}/groups/remove-all", async Task<IResult> (string id, EndpointScopedRequest body, MatterControllerService service) =>
        {
            if (ValidateEndpointScopedRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceInvokeOnlyCommandResult(await service.RemoveAllGroupsAsync(id, body.Endpoint));
        })
            .WithSummary("Remove all groups")
            .WithDescription("Runs RemoveAllGroups on one application endpoint.");

        devices.MapPost("/devices/{id}/groups/add-if-identifying", async Task<IResult> (string id, GroupAddRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupAddRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceInvokeOnlyCommandResult(await service.AddGroupIfIdentifyingAsync(id, body.Endpoint, body.GroupId, body.GroupName));
        })
            .WithSummary("Add one group if identifying")
            .WithDescription("Runs AddGroupIfIdentifying on one application endpoint.");

        devices.MapPost("/devices/{id}/group-keys/write", async Task<IResult> (string id, GroupKeySetWriteRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupKeySetWriteRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            if (!TryParseRequiredHexBytes(body.EpochKey0Hex, out var epochKey0))
            {
                return Results.BadRequest(new ErrorResponse("EpochKey0Hex must be valid even-length hexadecimal"));
            }

            if (epochKey0.Length != 16)
            {
                return Results.BadRequest(new ErrorResponse("EpochKey0Hex must decode to 16 bytes"));
            }

            if (!TryParseOptionalHexBytesLocal(body.EpochKey1Hex, out var epochKey1))
            {
                return Results.BadRequest(new ErrorResponse("EpochKey1Hex must be valid even-length hexadecimal"));
            }

            if (epochKey1 is { Length: not 16 })
            {
                return Results.BadRequest(new ErrorResponse("EpochKey1Hex must decode to 16 bytes when provided"));
            }

            if (!TryParseOptionalHexBytesLocal(body.EpochKey2Hex, out var epochKey2))
            {
                return Results.BadRequest(new ErrorResponse("EpochKey2Hex must be valid even-length hexadecimal"));
            }

            if (epochKey2 is { Length: not 16 })
            {
                return Results.BadRequest(new ErrorResponse("EpochKey2Hex must decode to 16 bytes when provided"));
            }

            if (!Enum.TryParse<GroupKeyManagementCluster.GroupKeySecurityPolicyEnum>(body.GroupKeySecurityPolicy, ignoreCase: true, out var securityPolicy))
            {
                return Results.BadRequest(new ErrorResponse($"Unknown GroupKeySecurityPolicy '{body.GroupKeySecurityPolicy}'"));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceInvokeOnlyCommandResult(await service.WriteGroupKeySetAsync(
                id,
                new MatterGroupKeySet(
                    body.GroupKeySetId,
                    securityPolicy,
                    epochKey0,
                    body.EpochStartTime0,
                    epochKey1,
                    body.EpochStartTime1,
                    epochKey2,
                    body.EpochStartTime2)));
        })
            .WithSummary("Write one group key set")
            .WithDescription("Runs KeySetWrite on endpoint 0 using hexadecimal epoch keys and explicit epoch start times.");

        devices.MapPost("/devices/{id}/group-keys/read", async Task<IResult> (string id, GroupKeySetIdRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupKeySetIdRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupKeySetReadResult(await service.ReadGroupKeySetAsync(id, body.GroupKeySetId));
        })
            .WithSummary("Read one group key set")
            .WithDescription("Runs KeySetRead on endpoint 0.");

        devices.MapPost("/devices/{id}/group-keys/remove", async Task<IResult> (string id, GroupKeySetIdRequest body, MatterControllerService service) =>
        {
            if (ValidateGroupKeySetIdRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceInvokeOnlyCommandResult(await service.RemoveGroupKeySetAsync(id, body.GroupKeySetId));
        })
            .WithSummary("Remove one group key set")
            .WithDescription("Runs KeySetRemove on endpoint 0.");

        devices.MapPost("/devices/{id}/group-keys/read-all-indices", async Task<IResult> (string id, MatterControllerService service) =>
        {
            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceGroupKeySetIndicesResult(await service.ReadAllGroupKeySetIndicesAsync(id));
        })
            .WithSummary("Read all group key set identifiers")
            .WithDescription("Runs KeySetReadAllIndices on endpoint 0.");

        devices.MapPost("/devices/{id}/scenes/add", async Task<IResult> (string id, SceneAddRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneAddRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneCommandResult(await service.AddSceneAsync(id, body));
        })
            .WithSummary("Add one scene")
            .WithDescription("Runs AddScene on one application endpoint using the explicit typed extension-field-set contract.");

        devices.MapPost("/devices/{id}/scenes/view", async Task<IResult> (string id, SceneCommandRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneCommandRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneViewResult(await service.ViewSceneAsync(id, body.Endpoint, body.GroupId, body.SceneId));
        })
            .WithSummary("View one scene")
            .WithDescription("Runs ViewScene on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/remove", async Task<IResult> (string id, SceneCommandRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneCommandRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneCommandResult(await service.RemoveSceneAsync(id, body.Endpoint, body.GroupId, body.SceneId));
        })
            .WithSummary("Remove one scene")
            .WithDescription("Runs RemoveScene on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/remove-all", async Task<IResult> (string id, SceneGroupRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneGroupRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneCommandResult(await service.RemoveAllScenesAsync(id, body.Endpoint, body.GroupId));
        })
            .WithSummary("Remove all scenes for one group")
            .WithDescription("Runs RemoveAllScenes on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/store", async Task<IResult> (string id, SceneCommandRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneCommandRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneCommandResult(await service.StoreSceneAsync(id, body.Endpoint, body.GroupId, body.SceneId));
        })
            .WithSummary("Store one scene")
            .WithDescription("Runs StoreScene on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/recall", async Task<IResult> (string id, SceneRecallRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneRecallRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceInvokeOnlyCommandResult(await service.RecallSceneAsync(id, body));
        })
            .WithSummary("Recall one scene")
            .WithDescription("Runs RecallScene on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/membership", async Task<IResult> (string id, SceneGroupRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneGroupRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneMembershipResult(await service.GetSceneMembershipAsync(id, body.Endpoint, body.GroupId));
        })
            .WithSummary("Read scene membership")
            .WithDescription("Runs GetSceneMembership on one application endpoint.");

        devices.MapPost("/devices/{id}/scenes/copy", async Task<IResult> (string id, SceneCopyRequest body, MatterControllerService service) =>
        {
            if (ValidateSceneCopyRequest(body) is { } validationError)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var missing = EnsureKnownDevice(id, service);
            if (missing != null)
            {
                return missing;
            }

            return ApiEndpointResults.MapDeviceSceneCopyResult(await service.CopySceneAsync(id, body));
        })
            .WithSummary("Copy one scene or all scenes")
            .WithDescription("Runs CopyScene on one application endpoint.");
    }

    private static bool TryParseOptionalHexBytesLocal(string? value, out byte[]? bytes)
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
