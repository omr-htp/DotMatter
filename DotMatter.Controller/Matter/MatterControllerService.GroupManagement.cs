using DotMatter.Core;
using DotMatter.Core.Clusters;

namespace DotMatter.Controller;

public sealed partial class MatterControllerService
{
    internal async Task<DeviceGroupsQueryResult> ReadGroupsStateAsync(string id, ushort endpoint)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, GroupsCluster.ClusterId, "Groups", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var groupKeyError = GetClusterSupportError(prepared.Topology!, endpointId: 0, GroupKeyManagementCluster.ClusterId, "Group Key Management");
            if (groupKeyError is not null)
            {
                return new(false, DeviceOperationFailure.Unsupported, null, groupKeyError);
            }

            var groupsState = await MatterAdministration.ReadGroupsStateAsync(prepared.Session!, endpoint, cts.Token);
            var groupKeyState = await MatterAdministration.ReadGroupKeyManagementStateAsync(prepared.Session!, endpointId: 0, cts.Token);
            var device = Registry.Get(id);

            return new(true, DeviceOperationFailure.None, new DeviceGroupsStateResponse(
                id,
                device?.Name,
                MapGroupsState(endpoint, groupsState, groupKeyState)));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Groups read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceGroupKeyManagementQueryResult> ReadGroupKeyManagementStateAsync(string id)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpointId: 0, GroupKeyManagementCluster.ClusterId, "Group Key Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var state = await MatterAdministration.ReadGroupKeyManagementStateAsync(prepared.Session!, endpointId: 0, cts.Token);
            var device = Registry.Get(id);
            return new(true, DeviceOperationFailure.None, new DeviceGroupKeyManagementStateResponse(
                id,
                device?.Name,
                MapGroupKeyManagementState(state)));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Group key management read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceScenesQueryResult> ReadScenesStateAsync(string id, ushort endpoint)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var state = await MatterAdministration.ReadScenesStateAsync(prepared.Session!, endpoint, cts.Token);
            var device = Registry.Get(id);
            return new(true, DeviceOperationFailure.None, new DeviceScenesStateResponse(
                id,
                device?.Name,
                MapScenesState(endpoint, state)));
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "Scenes read timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceGroupCommandResult> AddGroupAsync(string id, ushort endpoint, ushort groupId, string groupName)
    {
        return await ExecuteGroupCommandAsync(
            id,
            endpoint,
            "AddGroup",
            ct => MatterAdministration.AddGroupAsync(GetRequiredSessionOwner(id), endpoint, groupId, groupName, ct),
            successEvent: $"groups:add:{endpoint}:{groupId}");
    }

    internal async Task<DeviceGroupCommandResult> ViewGroupAsync(string id, ushort endpoint, ushort groupId)
    {
        return await ExecuteGroupCommandAsync(
            id,
            endpoint,
            "ViewGroup",
            ct => MatterAdministration.ViewGroupAsync(GetRequiredSessionOwner(id), endpoint, groupId, ct),
            successEvent: null);
    }

    internal async Task<DeviceGroupMembershipResult> GetGroupMembershipAsync(string id, ushort endpoint, ushort[]? groupIds)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, GroupsCluster.ClusterId, "Groups", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.GetGroupMembershipAsync(prepared.Session!, endpoint, groupIds, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceGroupMembershipResponse(
                id,
                device?.Name,
                endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Capacity,
                [.. result.GroupIds],
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "groups", $"membership:{endpoint}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "GetGroupMembership failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "GetGroupMembership timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceGroupCommandResult> RemoveGroupAsync(string id, ushort endpoint, ushort groupId)
    {
        return await ExecuteGroupCommandAsync(
            id,
            endpoint,
            "RemoveGroup",
            ct => MatterAdministration.RemoveGroupAsync(GetRequiredSessionOwner(id), endpoint, groupId, ct),
            successEvent: $"groups:remove:{endpoint}:{groupId}");
    }

    internal async Task<DeviceInvokeOnlyCommandResult> RemoveAllGroupsAsync(string id, ushort endpoint)
    {
        return await ExecuteInvokeOnlyCommandAsync(
            id,
            endpoint,
            GroupsCluster.ClusterId,
            "Groups",
            "RemoveAllGroups",
            ct => MatterAdministration.RemoveAllGroupsAsync(GetRequiredSessionOwner(id), endpoint, ct),
            successEvent: $"groups:remove-all:{endpoint}");
    }

    internal async Task<DeviceInvokeOnlyCommandResult> AddGroupIfIdentifyingAsync(string id, ushort endpoint, ushort groupId, string groupName)
    {
        return await ExecuteInvokeOnlyCommandAsync(
            id,
            endpoint,
            GroupsCluster.ClusterId,
            "Groups",
            "AddGroupIfIdentifying",
            ct => MatterAdministration.AddGroupIfIdentifyingAsync(GetRequiredSessionOwner(id), endpoint, groupId, groupName, ct),
            successEvent: $"groups:add-if-identifying:{endpoint}:{groupId}");
    }

    internal async Task<DeviceInvokeOnlyCommandResult> WriteGroupKeySetAsync(string id, MatterGroupKeySet groupKeySet)
    {
        return await ExecuteInvokeOnlyCommandAsync(
            id,
            endpoint: 0,
            GroupKeyManagementCluster.ClusterId,
            "Group Key Management",
            "KeySetWrite",
            ct => MatterAdministration.WriteGroupKeySetAsync(GetRequiredSessionOwner(id), groupKeySet, endpointId: 0, ct),
            successEvent: $"group-keys:write:{groupKeySet.GroupKeySetId}");
    }

    internal async Task<DeviceGroupKeySetReadResult> ReadGroupKeySetAsync(string id, ushort groupKeySetId)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpointId: 0, GroupKeyManagementCluster.ClusterId, "Group Key Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.ReadGroupKeySetAsync(prepared.Session!, groupKeySetId, endpointId: 0, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceGroupKeySetReadResponse(
                id,
                device?.Name,
                0,
                result.InvokeSucceeded,
                result.Accepted,
                result.GroupKeySet is null ? null : MapGroupKeySet(result.GroupKeySet),
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "group-keys", $"read:{groupKeySetId}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "KeySetRead failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "KeySetRead timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceInvokeOnlyCommandResult> RemoveGroupKeySetAsync(string id, ushort groupKeySetId)
    {
        return await ExecuteInvokeOnlyCommandAsync(
            id,
            endpoint: 0,
            GroupKeyManagementCluster.ClusterId,
            "Group Key Management",
            "KeySetRemove",
            ct => MatterAdministration.RemoveGroupKeySetAsync(GetRequiredSessionOwner(id), groupKeySetId, endpointId: 0, ct),
            successEvent: $"group-keys:remove:{groupKeySetId}");
    }

    internal async Task<DeviceGroupKeySetIndicesResult> ReadAllGroupKeySetIndicesAsync(string id)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpointId: 0, GroupKeyManagementCluster.ClusterId, "Group Key Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.ReadAllGroupKeySetIndicesAsync(prepared.Session!, endpointId: 0, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceGroupKeySetIndicesResponse(
                id,
                device?.Name,
                0,
                result.InvokeSucceeded,
                result.Accepted,
                [.. result.GroupKeySetIds.Select(MapGroupKeySetIndex)],
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "group-keys", "read-all-indices");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "KeySetReadAllIndices failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "KeySetReadAllIndices timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceSceneCommandResult> AddSceneAsync(string id, SceneAddRequest body)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, body.Endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.AddSceneAsync(
                prepared.Session!,
                body.Endpoint,
                body.GroupId,
                body.SceneId,
                body.TransitionTime,
                body.SceneName,
                [.. body.ExtensionFieldSets.Select(ToMatterSceneExtensionFieldSet)],
                cts.Token);
            var device = Registry.Get(id);
            var response = MapSceneCommandResponse(id, device?.Name, body.Endpoint, result);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "scenes", $"add:{body.Endpoint}:{body.GroupId}:{body.SceneId}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "AddScene failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "AddScene timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceSceneViewResult> ViewSceneAsync(string id, ushort endpoint, ushort groupId, byte sceneId)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.ViewSceneAsync(prepared.Session!, endpoint, groupId, sceneId, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceSceneViewResponse(
                id,
                device?.Name,
                endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Status?.ToString(),
                result.GroupId,
                FormatGroupIdHex(result.GroupId),
                result.SceneId,
                result.TransitionTime,
                result.SceneName,
                [.. result.ExtensionFieldSets.Select(MapSceneExtensionFieldSet)],
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "scenes", $"view:{endpoint}:{groupId}:{sceneId}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "ViewScene failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "ViewScene timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceSceneCommandResult> RemoveSceneAsync(string id, ushort endpoint, ushort groupId, byte sceneId)
    {
        return await ExecuteSceneCommandAsync(
            id,
            endpoint,
            "RemoveScene",
            ct => MatterAdministration.RemoveSceneAsync(GetRequiredSessionOwner(id), endpoint, groupId, sceneId, ct),
            successEvent: $"scenes:remove:{endpoint}:{groupId}:{sceneId}");
    }

    internal async Task<DeviceSceneCommandResult> RemoveAllScenesAsync(string id, ushort endpoint, ushort groupId)
    {
        return await ExecuteSceneCommandAsync(
            id,
            endpoint,
            "RemoveAllScenes",
            ct => MatterAdministration.RemoveAllScenesAsync(GetRequiredSessionOwner(id), endpoint, groupId, ct),
            successEvent: $"scenes:remove-all:{endpoint}:{groupId}");
    }

    internal async Task<DeviceSceneCommandResult> StoreSceneAsync(string id, ushort endpoint, ushort groupId, byte sceneId)
    {
        return await ExecuteSceneCommandAsync(
            id,
            endpoint,
            "StoreScene",
            ct => MatterAdministration.StoreSceneAsync(GetRequiredSessionOwner(id), endpoint, groupId, sceneId, ct),
            successEvent: $"scenes:store:{endpoint}:{groupId}:{sceneId}");
    }

    internal async Task<DeviceInvokeOnlyCommandResult> RecallSceneAsync(string id, SceneRecallRequest body)
    {
        return await ExecuteInvokeOnlyCommandAsync(
            id,
            body.Endpoint,
            ScenesManagementCluster.ClusterId,
            "Scenes Management",
            "RecallScene",
            ct => MatterAdministration.RecallSceneAsync(GetRequiredSessionOwner(id), body.Endpoint, body.GroupId, body.SceneId, body.TransitionTime, ct),
            successEvent: $"scenes:recall:{body.Endpoint}:{body.GroupId}:{body.SceneId}");
    }

    internal async Task<DeviceSceneMembershipResult> GetSceneMembershipAsync(string id, ushort endpoint, ushort groupId)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.GetSceneMembershipAsync(prepared.Session!, endpoint, groupId, cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceSceneMembershipResponse(
                id,
                device?.Name,
                endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Status?.ToString(),
                result.Capacity,
                result.GroupId,
                FormatGroupIdHex(result.GroupId),
                [.. result.SceneIds.Select(static scene => (int)scene)],
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "scenes", $"membership:{endpoint}:{groupId}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "GetSceneMembership failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "GetSceneMembership timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    internal async Task<DeviceSceneCopyResult> CopySceneAsync(string id, SceneCopyRequest body)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, body.Endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await MatterAdministration.CopySceneAsync(
                prepared.Session!,
                body.Endpoint,
                body.CopyAllScenes,
                body.GroupIdentifierFrom,
                body.SceneIdentifierFrom,
                body.GroupIdentifierTo,
                body.SceneIdentifierTo,
                cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceSceneCopyResponse(
                id,
                device?.Name,
                body.Endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Status?.ToString(),
                result.GroupIdentifierFrom,
                FormatGroupIdHex(result.GroupIdentifierFrom),
                result.SceneIdentifierFrom,
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "scenes", $"copy:{body.Endpoint}:{body.GroupIdentifierFrom}:{body.SceneIdentifierFrom}");
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? "CopyScene failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, "CopyScene timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    private async Task<DeviceGroupCommandResult> ExecuteGroupCommandAsync(
        string id,
        ushort endpoint,
        string operationName,
        Func<CancellationToken, Task<MatterGroupCommandResult>> executeAsync,
        string? successEvent)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, GroupsCluster.ClusterId, "Groups", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await executeAsync(cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceGroupCommandResponse(
                id,
                device?.Name,
                endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Status?.ToString(),
                result.GroupId,
                FormatGroupIdHex(result.GroupId),
                result.GroupName,
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                if (!string.IsNullOrWhiteSpace(successEvent))
                {
                    PublishEvent(id, "groups", successEvent);
                }

                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? $"{operationName} failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, $"{operationName} timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    private async Task<DeviceSceneCommandResult> ExecuteSceneCommandAsync(
        string id,
        ushort endpoint,
        string operationName,
        Func<CancellationToken, Task<MatterSceneCommandResult>> executeAsync,
        string successEvent)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, ScenesManagementCluster.ClusterId, "Scenes Management", cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await executeAsync(cts.Token);
            var device = Registry.Get(id);
            var response = MapSceneCommandResponse(id, device?.Name, endpoint, result);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, "scenes", successEvent);
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? $"{operationName} failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, $"{operationName} timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    private async Task<DeviceInvokeOnlyCommandResult> ExecuteInvokeOnlyCommandAsync(
        string id,
        ushort endpoint,
        uint clusterId,
        string clusterName,
        string operationName,
        Func<CancellationToken, Task<MatterInvokeCommandResult>> executeAsync,
        string successEvent)
    {
        try
        {
            using var cts = new CancellationTokenSource(_apiOptions.CommandTimeout);
            var prepared = await PrepareSupportedClusterAsync(id, endpoint, clusterId, clusterName, cts.Token);
            if (!prepared.Success)
            {
                return new(false, prepared.Failure, null, prepared.Error);
            }

            var result = await executeAsync(cts.Token);
            var device = Registry.Get(id);
            var response = new DeviceInvokeOnlyCommandResponse(
                id,
                device?.Name,
                endpoint,
                result.InvokeSucceeded,
                result.Accepted,
                result.Error);

            if (result.Accepted)
            {
                Registry.Update(id, current => current.LastSeen = DateTime.UtcNow);
                PublishEvent(id, clusterId == GroupKeyManagementCluster.ClusterId ? "group-keys" : clusterId == ScenesManagementCluster.ClusterId ? "scenes" : "groups", successEvent);
                return new(true, DeviceOperationFailure.None, response);
            }

            return new(false, DeviceOperationFailure.TransportError, response, result.Error ?? $"{operationName} failed");
        }
        catch (OperationCanceledException)
        {
            return new(false, DeviceOperationFailure.Timeout, null, $"{operationName} timed out");
        }
        catch (Exception ex)
        {
            return new(false, DeviceOperationFailure.TransportError, null, ex.Message);
        }
    }

    private sealed record SupportedClusterPreparation(
        bool Success,
        ResilientSession? Session,
        MatterDeviceTopology? Topology,
        DeviceOperationFailure Failure,
        string? Error);

    private async Task<SupportedClusterPreparation> PrepareSupportedClusterAsync(
        string id,
        ushort endpointId,
        uint clusterId,
        string clusterName,
        CancellationToken ct)
    {
        if (!TryGetSessionOwner(id, out var session))
        {
            return new SupportedClusterPreparation(false, null, null, DeviceOperationFailure.NotConnected, "Device is not connected");
        }

        var topology = await MatterTopology.DescribeAsync(session, ct);
        if (GetClusterSupportError(topology, endpointId, clusterId, clusterName) is { } error)
        {
            return new SupportedClusterPreparation(false, session, topology, DeviceOperationFailure.Unsupported, error);
        }

        return new SupportedClusterPreparation(true, session, topology, DeviceOperationFailure.None, null);
    }

    private static string? GetClusterSupportError(MatterDeviceTopology topology, ushort endpointId, uint clusterId, string clusterName)
    {
        var endpoint = MatterTopology.FindEndpoint(topology, endpointId);
        if (endpoint is null)
        {
            return $"Endpoint {endpointId} was not found on the device";
        }

        return endpoint.ServerClusters.Contains(clusterId)
            ? null
            : $"Endpoint {endpointId} does not host the {clusterName} cluster";
    }

    private static DeviceGroupsState MapGroupsState(ushort endpoint, MatterGroupsState groupsState, MatterGroupKeyManagementState groupKeyState)
    {
        var keyMap = groupKeyState.GroupKeyMap.ToDictionary(entry => entry.GroupId, entry => entry.GroupKeySetId);
        var groups = groupKeyState.GroupTable
            .Where(entry => entry.Endpoints.Contains(endpoint))
            .OrderBy(entry => entry.GroupId)
            .Select(entry => new DeviceGroupMembershipEntry(
                entry.GroupId,
                FormatGroupIdHex(entry.GroupId)!,
                entry.GroupName,
                keyMap.TryGetValue(entry.GroupId, out var groupKeySetId) ? groupKeySetId : null,
                keyMap.TryGetValue(entry.GroupId, out groupKeySetId) ? FormatGroupIdHex(groupKeySetId) : null))
            .ToArray();

        return new DeviceGroupsState(
            endpoint,
            ExpandNameSupport(groupsState.NameSupport),
            groups);
    }

    private static DeviceGroupKeyManagementState MapGroupKeyManagementState(MatterGroupKeyManagementState state)
        => new(
            Endpoint: 0,
            GroupKeyMap: [.. state.GroupKeyMap
                .OrderBy(entry => entry.GroupId)
                .ThenBy(entry => entry.GroupKeySetId)
                .Select(static entry => new DeviceGroupKeyMapEntry(
                    entry.GroupId,
                    $"0x{entry.GroupId:X4}",
                    entry.GroupKeySetId,
                    $"0x{entry.GroupKeySetId:X4}"))],
            GroupTable: [.. state.GroupTable
                .OrderBy(entry => entry.GroupId)
                .Select(static entry => new DeviceGroupTableEntry(
                    entry.GroupId,
                    $"0x{entry.GroupId:X4}",
                    [.. entry.Endpoints.OrderBy(static endpointId => endpointId)],
                    entry.GroupName))],
            MaxGroupsPerFabric: state.MaxGroupsPerFabric,
            MaxGroupKeysPerFabric: state.MaxGroupKeysPerFabric);

    private static DeviceScenesState MapScenesState(ushort endpoint, MatterScenesState state)
        => new(
            endpoint,
            state.SceneTableSize,
            [.. state.FabricSceneInfo.Select(static scene => new DeviceSceneInfo(
                scene.SceneCount,
                scene.CurrentScene,
                scene.CurrentGroup,
                scene.SceneValid,
                scene.RemainingCapacity,
                scene.FabricIndex))]);

    private static DeviceGroupKeySet MapGroupKeySet(MatterGroupKeySet groupKeySet)
        => new(
            groupKeySet.GroupKeySetId,
            FormatGroupIdHex(groupKeySet.GroupKeySetId)!,
            groupKeySet.GroupKeySecurityPolicy.ToString(),
            ToHex(groupKeySet.EpochKey0),
            groupKeySet.EpochStartTime0,
            ToHex(groupKeySet.EpochKey1),
            groupKeySet.EpochStartTime1,
            ToHex(groupKeySet.EpochKey2),
            groupKeySet.EpochStartTime2);

    private static DeviceGroupKeySetIndex MapGroupKeySetIndex(ushort groupKeySetId)
        => new(groupKeySetId, FormatGroupIdHex(groupKeySetId)!);

    private static MatterSceneExtensionFieldSet ToMatterSceneExtensionFieldSet(SceneExtensionFieldSetRequest request)
        => new(
            request.ClusterId,
            [.. request.AttributeValues.Select(ToMatterSceneAttributeValue)]);

    private static MatterSceneAttributeValue ToMatterSceneAttributeValue(SceneAttributeValueRequest request)
        => new(
            request.AttributeId,
            request.ValueUnsigned8,
            request.ValueSigned8,
            request.ValueUnsigned16,
            request.ValueSigned16,
            request.ValueUnsigned32,
            request.ValueSigned32,
            request.ValueUnsigned64,
            request.ValueSigned64);

    private static DeviceSceneCommandResponse MapSceneCommandResponse(string id, string? deviceName, ushort endpoint, MatterSceneCommandResult result)
        => new(
            id,
            deviceName,
            endpoint,
            result.InvokeSucceeded,
            result.Accepted,
            result.Status?.ToString(),
            result.GroupId,
            FormatGroupIdHex(result.GroupId),
            result.SceneId,
            result.Error);

    private static DeviceSceneExtensionFieldSet MapSceneExtensionFieldSet(MatterSceneExtensionFieldSet extensionFieldSet)
        => new(
            extensionFieldSet.ClusterId,
            $"0x{extensionFieldSet.ClusterId:X4}",
            [.. extensionFieldSet.AttributeValues.Select(MapSceneAttributeValue)]);

    private static DeviceSceneAttributeValue MapSceneAttributeValue(MatterSceneAttributeValue attributeValue)
        => new(
            attributeValue.AttributeId,
            $"0x{attributeValue.AttributeId:X4}",
            attributeValue.ValueUnsigned8,
            attributeValue.ValueSigned8,
            attributeValue.ValueUnsigned16,
            attributeValue.ValueSigned16,
            attributeValue.ValueUnsigned32,
            attributeValue.ValueSigned32,
            attributeValue.ValueUnsigned64,
            attributeValue.ValueSigned64);

    private static string[] ExpandNameSupport(GroupsCluster.NameSupportBitmap nameSupport)
    {
        if (nameSupport == 0)
        {
            return [];
        }

        return [.. Enum.GetValues<GroupsCluster.NameSupportBitmap>()
            .Where(flag => flag != 0 && nameSupport.HasFlag(flag))
            .Select(static flag => flag.ToString())];
    }

    private static string? FormatGroupIdHex(ushort? groupId)
        => groupId.HasValue ? $"0x{groupId.Value:X4}" : null;
}
