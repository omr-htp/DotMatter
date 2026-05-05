using System.Globalization;
using DotMatter.Core.Clusters;

namespace DotMatter.Controller;

internal static partial class DeviceApiEndpoints
{
    internal static void MapDeviceApiEndpoints(this RouteGroupBuilder api)
    {
        var devices = api.MapGroup(string.Empty).WithTags("Devices");
        MapDeviceOverviewEndpoints(devices);
        MapDeviceControlEndpoints(devices);
        MapDeviceAdminEndpoints(devices);
        MapDeviceNetworkCommissioningEndpoints(devices);
        MapDeviceGroupManagementEndpoints(devices);
        MapDeviceBindingAndEventEndpoints(devices);
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

    private static bool TryParseRequiredHexBytes(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? ValidateEnhancedCommissioningWindowRequest(EnhancedCommissioningWindowRequest body)
    {
        if (body.CommissioningTimeout == 0)
        {
            return "CommissioningTimeout must be greater than 0";
        }

        if (body.Discriminator > 0x0FFF)
        {
            return "Discriminator must be in the 0-4095 range";
        }

        if (body.Iterations == 0)
        {
            return "Iterations must be greater than 0";
        }

        if (string.IsNullOrWhiteSpace(body.PakePasscodeVerifierHex))
        {
            return "PakePasscodeVerifierHex is required";
        }

        if (string.IsNullOrWhiteSpace(body.SaltHex))
        {
            return "SaltHex is required";
        }

        return null;
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

    private static string? ValidateNetworkCommissioningScanRequest(NetworkCommissioningScanRequest body)
    {
        if (body.Ssid is null)
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetByteCount(body.Ssid) > 32
            ? "Ssid must encode to at most 32 bytes"
            : null;
    }

    private static string? ValidateNetworkCommissioningWiFiRequest(NetworkCommissioningWiFiRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Ssid))
        {
            return "Ssid is required";
        }

        if (System.Text.Encoding.UTF8.GetByteCount(body.Ssid) > 32)
        {
            return "Ssid must encode to at most 32 bytes";
        }

        return System.Text.Encoding.UTF8.GetByteCount(body.Credentials ?? string.Empty) > 64
            ? "Credentials must encode to at most 64 bytes"
            : null;
    }

    private static string? ValidateNetworkCommissioningThreadRequest(NetworkCommissioningThreadRequest body)
        => string.IsNullOrWhiteSpace(body.OperationalDatasetHex)
            ? "OperationalDatasetHex is required"
            : null;

    private static string? ValidateNetworkCommissioningNetworkIdRequest(NetworkCommissioningNetworkIdRequest body)
        => string.IsNullOrWhiteSpace(body.NetworkIdHex)
            ? "NetworkIdHex is required"
            : null;

    private static string? ValidateNetworkCommissioningReorderRequest(NetworkCommissioningReorderRequest body)
        => string.IsNullOrWhiteSpace(body.NetworkIdHex)
            ? "NetworkIdHex is required"
            : null;

    private static string? ValidateEndpoint(ushort endpoint)
        => endpoint == 0
            ? "Endpoint must be greater than 0"
            : null;

    private static string? ValidateGroupAddRequest(GroupAddRequest body)
    {
        if (ValidateEndpoint(body.Endpoint) is { } endpointError)
        {
            return endpointError;
        }

        return string.IsNullOrWhiteSpace(body.GroupName)
            ? "GroupName is required"
            : null;
    }

    private static string? ValidateGroupCommandRequest(GroupCommandRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateGroupMembershipRequest(GroupMembershipRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateEndpointScopedRequest(EndpointScopedRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateGroupKeySetWriteRequest(GroupKeySetWriteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.GroupKeySecurityPolicy))
        {
            return "GroupKeySecurityPolicy is required";
        }

        if (!Enum.TryParse<GroupKeyManagementCluster.GroupKeySecurityPolicyEnum>(body.GroupKeySecurityPolicy, ignoreCase: true, out _))
        {
            return $"Unknown GroupKeySecurityPolicy '{body.GroupKeySecurityPolicy}'";
        }

        if (string.IsNullOrWhiteSpace(body.EpochKey0Hex))
        {
            return "EpochKey0Hex is required";
        }

        if (!string.IsNullOrWhiteSpace(body.EpochKey1Hex) ^ body.EpochStartTime1.HasValue)
        {
            return "EpochKey1Hex and EpochStartTime1 must be provided together";
        }

        return !string.IsNullOrWhiteSpace(body.EpochKey2Hex) ^ body.EpochStartTime2.HasValue
            ? "EpochKey2Hex and EpochStartTime2 must be provided together"
            : null;
    }

    private static string? ValidateGroupKeySetIdRequest(GroupKeySetIdRequest body)
        => body.GroupKeySetId == 0
            ? "GroupKeySetId must be greater than 0"
            : null;

    private static string? ValidateSceneAttributeValueRequest(SceneAttributeValueRequest body)
    {
        var valueCount = 0;
        if (body.ValueUnsigned8 is not null)
        {
            valueCount++;
        }

        if (body.ValueSigned8 is not null)
        {
            valueCount++;
        }

        if (body.ValueUnsigned16 is not null)
        {
            valueCount++;
        }

        if (body.ValueSigned16 is not null)
        {
            valueCount++;
        }

        if (body.ValueUnsigned32 is not null)
        {
            valueCount++;
        }

        if (body.ValueSigned32 is not null)
        {
            valueCount++;
        }

        if (body.ValueUnsigned64 is not null)
        {
            valueCount++;
        }

        if (body.ValueSigned64 is not null)
        {
            valueCount++;
        }

        return valueCount == 1
            ? null
            : "Each scene attribute value must set exactly one Value* field";
    }

    private static string? ValidateSceneExtensionFieldSetRequest(SceneExtensionFieldSetRequest body)
    {
        if (body.AttributeValues is null)
        {
            return "AttributeValues is required";
        }

        foreach (var attributeValue in body.AttributeValues)
        {
            if (ValidateSceneAttributeValueRequest(attributeValue) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidateSceneAddRequest(SceneAddRequest body)
    {
        if (ValidateEndpoint(body.Endpoint) is { } endpointError)
        {
            return endpointError;
        }

        if (string.IsNullOrWhiteSpace(body.SceneName))
        {
            return "SceneName is required";
        }

        if (body.ExtensionFieldSets is null)
        {
            return "ExtensionFieldSets is required";
        }

        foreach (var extensionFieldSet in body.ExtensionFieldSets)
        {
            if (ValidateSceneExtensionFieldSetRequest(extensionFieldSet) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidateSceneCommandRequest(SceneCommandRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateSceneGroupRequest(SceneGroupRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateSceneRecallRequest(SceneRecallRequest body)
        => ValidateEndpoint(body.Endpoint);

    private static string? ValidateSceneCopyRequest(SceneCopyRequest body)
        => ValidateEndpoint(body.Endpoint);
}
