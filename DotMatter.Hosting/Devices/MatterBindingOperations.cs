using System.Globalization;
using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Hosting.Runtime;
using Org.BouncyCastle.Math;

namespace DotMatter.Hosting.Devices;

/// <summary>
/// Shared Matter Binding and Access Control helpers for hosted consumers.
/// </summary>
public static class MatterBindingOperations
{
    /// <summary>Converts a stored node identifier to its operational node-id value.</summary>
    public static ulong ToOperationalNodeId(string nodeId)
        => ToOperationalNodeId(new BigInteger(nodeId));

    /// <summary>Converts a Matter node identifier to its operational node-id value.</summary>
    public static ulong ToOperationalNodeId(BigInteger nodeId)
        => ulong.Parse(
            MatterDeviceHost.GetNodeOperationalId(nodeId),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

    /// <summary>Attempts to convert a stored node identifier to its operational node-id value.</summary>
    public static bool TryToOperationalNodeId(string nodeId, out ulong operationalNodeId)
    {
        operationalNodeId = 0;
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        try
        {
            operationalNodeId = ToOperationalNodeId(nodeId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Builds an operational node-id index for local device records.</summary>
    public static IReadOnlyDictionary<ulong, DeviceInfo> BuildOperationalNodeIndex(IEnumerable<DeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var index = new Dictionary<ulong, DeviceInfo>();
        foreach (var device in devices)
        {
            if (TryToOperationalNodeId(device.NodeId, out var operationalNodeId))
            {
                index[operationalNodeId] = device;
            }
        }

        return index;
    }

    /// <summary>Returns endpoints that host the Binding cluster, defaulting to endpoint 1.</summary>
    public static ushort[] GetBindingEndpoints(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.Endpoints is null)
        {
            return [1];
        }

        var endpoints = device.Endpoints
            .Where(static endpoint => endpoint.Key != 0 && endpoint.Value.Contains(BindingCluster.ClusterId))
            .Select(static endpoint => endpoint.Key)
            .Order()
            .ToArray();

        return endpoints.Length == 0 ? [1] : endpoints;
    }

    /// <summary>Ensures a source node has an OnOff Operate ACL grant on the target endpoint.</summary>
    public static async Task<WriteResponse> EnsureOnOffOperateAclAsync(
        ISession targetSession,
        ulong controllerNodeId,
        ulong sourceNodeId,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var accessControl = new AccessControlCluster(targetSession, endpointId: 0);
        var existingAcl = await accessControl.ReadACLAsync(ct) ?? [];
        var desiredTarget = new AccessControlCluster.AccessControlTargetStruct
        {
            Cluster = OnOffCluster.ClusterId,
            Endpoint = targetEndpoint,
        };

        if (existingAcl.Any(entry =>
                entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate
                && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
                && entry.Subjects?.Contains(sourceNodeId) == true
                && entry.Targets?.Any(target => target.Cluster == desiredTarget.Cluster && target.Endpoint == desiredTarget.Endpoint) == true))
        {
            return new WriteResponse(true, []);
        }

        var updatedAcl = existingAcl;
        if (!updatedAcl.Any(entry =>
                entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer
                && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
                && entry.Subjects?.Contains(controllerNodeId) == true))
        {
            updatedAcl = updatedAcl.Prepend(new AccessControlCluster.AccessControlEntryStruct
            {
                Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Administer,
                AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
                Subjects = [controllerNodeId],
                Targets = null,
            }).ToArray();
        }

        updatedAcl = updatedAcl.Append(new AccessControlCluster.AccessControlEntryStruct
        {
            Privilege = AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate,
            AuthMode = AccessControlCluster.AccessControlEntryAuthModeEnum.CASE,
            Subjects = [sourceNodeId],
            Targets = [desiredTarget],
        }).ToArray();

        return await accessControl.WriteACLAsync(updatedAcl, ct: ct);
    }

    /// <summary>Ensures a source endpoint has an OnOff Binding entry to the target node endpoint.</summary>
    public static async Task<WriteResponse> EnsureOnOffBindingAsync(
        ISession sourceSession,
        ulong targetNodeId,
        ushort sourceEndpoint,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var binding = new BindingCluster(sourceSession, sourceEndpoint);
        var existingBinding = await binding.ReadBindingAsync(ct) ?? [];

        if (existingBinding.Any(entry =>
                entry.Node == targetNodeId
                && entry.Endpoint == targetEndpoint
                && entry.Cluster == OnOffCluster.ClusterId))
        {
            return new WriteResponse(true, []);
        }

        var updatedBinding = existingBinding.Append(new BindingCluster.TargetStruct
        {
            Node = targetNodeId,
            Endpoint = targetEndpoint,
            Cluster = OnOffCluster.ClusterId,
        }).ToArray();

        return await binding.WriteBindingAsync(updatedBinding, ct: ct);
    }

    /// <summary>Removes an exact OnOff Binding entry from a source endpoint when present.</summary>
    public static async Task<WriteResponse> RemoveOnOffBindingEntryAsync(
        ISession sourceSession,
        ulong targetNodeId,
        ushort sourceEndpoint,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var binding = new BindingCluster(sourceSession, sourceEndpoint);
        var existingBinding = await binding.ReadBindingAsync(ct) ?? [];
        var updatedBinding = existingBinding
            .Where(entry => entry.Node != targetNodeId
                || entry.Endpoint != targetEndpoint
                || entry.Cluster != OnOffCluster.ClusterId)
            .ToArray();

        return updatedBinding.Length == existingBinding.Length
            ? new WriteResponse(true, [])
            : await binding.WriteBindingAsync(updatedBinding, ct: ct);
    }

    /// <summary>Removes an exact OnOff Operate ACL grant from the target endpoint when present.</summary>
    public static async Task<WriteResponse> RemoveOnOffOperateAclAsync(
        ISession targetSession,
        ulong sourceNodeId,
        ushort targetEndpoint,
        CancellationToken ct)
    {
        var accessControl = new AccessControlCluster(targetSession, endpointId: 0);
        var existingAcl = await accessControl.ReadACLAsync(ct) ?? [];
        var updatedAcl = existingAcl
            .Where(entry => !IsExactOnOffRouteAclEntry(entry, sourceNodeId, targetEndpoint))
            .ToArray();

        return updatedAcl.Length == existingAcl.Length
            ? new WriteResponse(true, [])
            : await accessControl.WriteACLAsync(updatedAcl, ct: ct);
    }

    /// <summary>Returns whether an ACL entry exactly matches a generated OnOff route grant.</summary>
    public static bool IsExactOnOffRouteAclEntry(
        AccessControlCluster.AccessControlEntryStruct entry,
        ulong sourceNodeId,
        ushort targetEndpoint)
        => entry.Privilege == AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate
            && entry.AuthMode == AccessControlCluster.AccessControlEntryAuthModeEnum.CASE
            && entry.AuxiliaryType is null
            && AclSubjectsExactlyMatch(entry.Subjects, [sourceNodeId])
            && AclTargetsExactlyMatch(entry.Targets, [new AccessControlCluster.AccessControlTargetStruct
            {
                Cluster = OnOffCluster.ClusterId,
                Endpoint = targetEndpoint,
            }]);

    /// <summary>Returns whether an ACL entry may still authorize the generated OnOff route.</summary>
    public static bool EntryMayAuthorizeOnOffRoute(
        AccessControlCluster.AccessControlEntryStruct entry,
        ulong sourceNodeId,
        ushort targetEndpoint)
    {
        if (entry.AuthMode != AccessControlCluster.AccessControlEntryAuthModeEnum.CASE)
        {
            return false;
        }

        if (entry.Privilege < AccessControlCluster.AccessControlEntryPrivilegeEnum.Operate)
        {
            return false;
        }

        if (entry.Subjects is { Length: > 0 } && !entry.Subjects.Contains(sourceNodeId))
        {
            return false;
        }

        if (entry.Targets is null || entry.Targets.Length == 0)
        {
            return true;
        }

        return entry.Targets.Any(target => TargetMayAuthorizeOnOffRoute(target, targetEndpoint));
    }

    /// <summary>Formats a Matter write response for app-level error messages.</summary>
    public static string FormatWriteResponse(WriteResponse response)
    {
        if (response.StatusCode is { } statusCode)
        {
            return $"status=0x{statusCode:X2}";
        }

        if (response.AttributeStatuses.Count == 0)
        {
            return "no write status returned";
        }

        return string.Join(", ", response.AttributeStatuses.Select(
            status => $"attr=0x{status.AttributeId:X4} status=0x{status.StatusCode:X2}"));
    }

    private static bool TargetMayAuthorizeOnOffRoute(
        AccessControlCluster.AccessControlTargetStruct target,
        ushort targetEndpoint)
    {
        var clusterMatches = !target.Cluster.HasValue || target.Cluster.Value == OnOffCluster.ClusterId;
        var endpointMatches = !target.Endpoint.HasValue || target.Endpoint.Value == targetEndpoint;
        return clusterMatches && endpointMatches;
    }

    private static bool AclSubjectsExactlyMatch(ulong[]? existingSubjects, ulong[] requestedSubjects)
    {
        if (requestedSubjects.Length == 0)
        {
            return existingSubjects is null || existingSubjects.Length == 0;
        }

        return existingSubjects is not null
            && existingSubjects.Order().SequenceEqual(requestedSubjects.Order());
    }

    private static bool AclTargetsExactlyMatch(
        AccessControlCluster.AccessControlTargetStruct[]? existingTargets,
        AccessControlCluster.AccessControlTargetStruct[] requestedTargets)
    {
        if (requestedTargets.Length == 0)
        {
            return existingTargets is null || existingTargets.Length == 0;
        }

        return existingTargets is not null
            && existingTargets
                .Select(NormalizeAclTarget)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(requestedTargets.Select(NormalizeAclTarget).Order(StringComparer.Ordinal));
    }

    private static string NormalizeAclTarget(AccessControlCluster.AccessControlTargetStruct target)
        => $"{target.Cluster?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{target.Endpoint?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{target.DeviceType?.ToString(CultureInfo.InvariantCulture) ?? "null"}";
}
