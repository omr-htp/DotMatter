using DotMatter.Core.Clusters;
using DotMatter.Core.InteractionModel;
using DotMatter.Core.Sessions;
using DotMatter.Core.TLV;

namespace DotMatter.Core;

/// <summary>Parameters for opening an enhanced commissioning window.</summary>
public sealed record MatterEnhancedCommissioningWindowParameters(
    ushort CommissioningTimeout,
    byte[] PakePasscodeVerifier,
    ushort Discriminator,
    uint Iterations,
    byte[] Salt);

/// <summary>Stable summary of the Basic Commissioning Info attribute.</summary>
public sealed record MatterBasicCommissioningInfo(
    ushort FailSafeExpiryLengthSeconds,
    ushort MaxCumulativeFailsafeSeconds);

/// <summary>Commissioning state gathered from the Administrator and General Commissioning clusters.</summary>
public sealed record MatterCommissioningState(
    AdministratorCommissioningCluster.CommissioningWindowStatusEnum WindowStatus,
    byte? AdminFabricIndex,
    ushort? AdminVendorId,
    MatterBasicCommissioningInfo? BasicCommissioningInfo,
    ushort TCAcceptedVersion,
    ushort TCMinRequiredVersion,
    ushort TCAcknowledgements,
    bool TCAcknowledgementsRequired,
    uint? TCUpdateDeadline);

/// <summary>Operational credential state gathered from the Operational Credentials cluster.</summary>
public sealed record MatterOperationalCredentialsState(
    IReadOnlyList<OperationalCredentialsCluster.FabricDescriptorStruct> Fabrics,
    IReadOnlyList<OperationalCredentialsCluster.NOCStruct> Nocs,
    IReadOnlyList<byte[]> TrustedRootCertificates,
    byte SupportedFabrics,
    byte CommissionedFabrics,
    byte CurrentFabricIndex);

/// <summary>One stored network entry reported by the Network Commissioning cluster.</summary>
public sealed record MatterNetworkCommissioningNetwork(
    byte[] NetworkId,
    bool Connected,
    byte[]? NetworkIdentifier,
    byte[]? ClientIdentifier);

/// <summary>Network commissioning state gathered from the root endpoint.</summary>
public sealed record MatterNetworkCommissioningState(
    NetworkCommissioningCluster.Feature? Features,
    IReadOnlyList<MatterNetworkCommissioningNetwork> Networks,
    byte MaxNetworks,
    byte ScanMaxTimeSeconds,
    byte ConnectMaxTimeSeconds,
    bool InterfaceEnabled,
    NetworkCommissioningCluster.NetworkCommissioningStatusEnum? LastNetworkingStatus,
    byte[]? LastNetworkId,
    int? LastConnectErrorValue,
    IReadOnlyList<NetworkCommissioningCluster.WiFiBandEnum> SupportedWiFiBands,
    NetworkCommissioningCluster.ThreadCapabilitiesBitmap? SupportedThreadFeatures,
    ushort? ThreadVersion);

/// <summary>One Wi-Fi scan result reported by Network Commissioning.</summary>
public sealed record MatterWiFiScanResult(
    NetworkCommissioningCluster.WiFiSecurityBitmap Security,
    byte[] Ssid,
    byte[] Bssid,
    ushort Channel,
    NetworkCommissioningCluster.WiFiBandEnum WiFiBand,
    sbyte Rssi);

/// <summary>One Thread scan result reported by Network Commissioning.</summary>
public sealed record MatterThreadScanResult(
    ushort PanId,
    ulong ExtendedPanId,
    string NetworkName,
    ushort Channel,
    byte Version,
    byte[] ExtendedAddress,
    sbyte Rssi,
    byte Lqi);

/// <summary>Typed result of a Network Commissioning scan command.</summary>
public sealed record MatterNetworkScanCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    NetworkCommissioningCluster.NetworkCommissioningStatusEnum? NetworkingStatus,
    string? DebugText,
    IReadOnlyList<MatterWiFiScanResult> WiFiScanResults,
    IReadOnlyList<MatterThreadScanResult> ThreadScanResults)
{
    /// <summary>True when the invoke succeeded and the cluster returned NetworkingStatus=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && NetworkingStatus == NetworkCommissioningCluster.NetworkCommissioningStatusEnum.Success;
}

/// <summary>Typed result of a Network Commissioning config-style command.</summary>
public sealed record MatterNetworkConfigCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    NetworkCommissioningCluster.NetworkCommissioningStatusEnum? NetworkingStatus,
    string? DebugText,
    byte? NetworkIndex,
    byte[]? ClientIdentity,
    byte[]? PossessionSignature)
{
    /// <summary>True when the invoke succeeded and the cluster returned NetworkingStatus=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && NetworkingStatus == NetworkCommissioningCluster.NetworkCommissioningStatusEnum.Success;
}

/// <summary>Typed result of a Network Commissioning connect command.</summary>
public sealed record MatterConnectNetworkCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    NetworkCommissioningCluster.NetworkCommissioningStatusEnum? NetworkingStatus,
    string? DebugText,
    int? ErrorValue)
{
    /// <summary>True when the invoke succeeded and the cluster returned NetworkingStatus=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && NetworkingStatus == NetworkCommissioningCluster.NetworkCommissioningStatusEnum.Success;
}

/// <summary>Groups cluster state for one application endpoint.</summary>
public sealed record MatterGroupsState(
    GroupsCluster.NameSupportBitmap NameSupport);

/// <summary>One Group Key Management map entry.</summary>
public sealed record MatterGroupKeyMapEntry(
    ushort GroupId,
    ushort GroupKeySetId);

/// <summary>One Group Key Management group-table entry.</summary>
public sealed record MatterGroupTableEntry(
    ushort GroupId,
    IReadOnlyList<ushort> Endpoints,
    string? GroupName);

/// <summary>Group Key Management state gathered from the root endpoint.</summary>
public sealed record MatterGroupKeyManagementState(
    IReadOnlyList<MatterGroupKeyMapEntry> GroupKeyMap,
    IReadOnlyList<MatterGroupTableEntry> GroupTable,
    ushort MaxGroupsPerFabric,
    ushort MaxGroupKeysPerFabric);

/// <summary>One fabric-scoped scene info entry.</summary>
public sealed record MatterSceneInfo(
    byte SceneCount,
    byte CurrentScene,
    ushort CurrentGroup,
    bool SceneValid,
    byte RemainingCapacity,
    byte FabricIndex);

/// <summary>Scenes Management state for one application endpoint.</summary>
public sealed record MatterScenesState(
    ushort SceneTableSize,
    IReadOnlyList<MatterSceneInfo> FabricSceneInfo);

/// <summary>One typed scene attribute-value entry.</summary>
public sealed record MatterSceneAttributeValue(
    uint AttributeId,
    byte? ValueUnsigned8 = null,
    sbyte? ValueSigned8 = null,
    ushort? ValueUnsigned16 = null,
    short? ValueSigned16 = null,
    uint? ValueUnsigned32 = null,
    int? ValueSigned32 = null,
    ulong? ValueUnsigned64 = null,
    long? ValueSigned64 = null);

/// <summary>One typed scene extension-field set.</summary>
public sealed record MatterSceneExtensionFieldSet(
    uint ClusterId,
    IReadOnlyList<MatterSceneAttributeValue> AttributeValues);

/// <summary>One Group Key Set payload.</summary>
public sealed record MatterGroupKeySet(
    ushort GroupKeySetId,
    GroupKeyManagementCluster.GroupKeySecurityPolicyEnum GroupKeySecurityPolicy,
    byte[]? EpochKey0,
    ulong? EpochStartTime0,
    byte[]? EpochKey1,
    ulong? EpochStartTime1,
    byte[]? EpochKey2,
    ulong? EpochStartTime2);

/// <summary>Typed result of an invoke-only command that relies on the interaction-model status only.</summary>
public sealed record MatterInvokeCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error)
{
    /// <summary>True when the invoke completed without an interaction-model failure.</summary>
    public bool Accepted => InvokeSucceeded && string.IsNullOrWhiteSpace(Error);
}

/// <summary>Typed result of a Groups command that returns a status and optional group metadata.</summary>
public sealed record MatterGroupCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterStatusCode? Status,
    ushort? GroupId,
    string? GroupName)
{
    /// <summary>True when the invoke succeeded and the cluster returned Status=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && Status == MatterStatusCode.Success;
}

/// <summary>Typed result of a Groups GetGroupMembership command.</summary>
public sealed record MatterGroupMembershipCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    byte? Capacity,
    IReadOnlyList<ushort> GroupIds)
{
    /// <summary>True when the invoke completed and the membership payload was decoded successfully.</summary>
    public bool Accepted => InvokeSucceeded && string.IsNullOrWhiteSpace(Error);
}

/// <summary>Typed result of a Group Key Management KeySetRead command.</summary>
public sealed record MatterGroupKeySetReadCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterGroupKeySet? GroupKeySet)
{
    /// <summary>True when the invoke completed and the key-set payload was decoded successfully.</summary>
    public bool Accepted => InvokeSucceeded && string.IsNullOrWhiteSpace(Error) && GroupKeySet is not null;
}

/// <summary>Typed result of a Group Key Management KeySetReadAllIndices command.</summary>
public sealed record MatterGroupKeySetReadAllIndicesCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    IReadOnlyList<ushort> GroupKeySetIds)
{
    /// <summary>True when the invoke completed and the key-set index list was decoded successfully.</summary>
    public bool Accepted => InvokeSucceeded && string.IsNullOrWhiteSpace(Error);
}

/// <summary>Typed result of a Scenes command that returns status plus target group/scene identifiers.</summary>
public sealed record MatterSceneCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterStatusCode? Status,
    ushort? GroupId,
    byte? SceneId)
{
    /// <summary>True when the invoke succeeded and the cluster returned Status=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && Status == MatterStatusCode.Success;
}

/// <summary>Typed result of a Scenes ViewScene command.</summary>
public sealed record MatterViewSceneCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterStatusCode? Status,
    ushort? GroupId,
    byte? SceneId,
    uint? TransitionTime,
    string? SceneName,
    IReadOnlyList<MatterSceneExtensionFieldSet> ExtensionFieldSets)
{
    /// <summary>True when the invoke succeeded and the cluster returned Status=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && Status == MatterStatusCode.Success;
}

/// <summary>Typed result of a Scenes GetSceneMembership command.</summary>
public sealed record MatterSceneMembershipCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterStatusCode? Status,
    byte? Capacity,
    ushort? GroupId,
    IReadOnlyList<byte> SceneIds)
{
    /// <summary>True when the invoke succeeded and the cluster returned Status=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && Status == MatterStatusCode.Success;
}

/// <summary>Typed result of a Scenes CopyScene command.</summary>
public sealed record MatterSceneCopyCommandResult(
    bool InvokeSucceeded,
    byte? InvokeStatusCode,
    string? Error,
    MatterStatusCode? Status,
    ushort? GroupIdentifierFrom,
    byte? SceneIdentifierFrom)
{
    /// <summary>True when the invoke succeeded and the cluster returned Status=Success.</summary>
    public bool Accepted => InvokeSucceeded
        && string.IsNullOrWhiteSpace(Error)
        && Status == MatterStatusCode.Success;
}

/// <summary>
/// High-level administration helpers for controller-facing commissioning and fabric-management flows.
/// </summary>
public static class MatterAdministration
{
    private const uint FeatureMapAttributeId = 0xFFFC;

    /// <summary>Read the current commissioning state from the root endpoint.</summary>
    public static async Task<MatterCommissioningState> ReadCommissioningStateAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var admin = new AdministratorCommissioningCluster(session, endpointId);
        var general = new GeneralCommissioningCluster(session, endpointId);

        var basicInfo = await general.ReadBasicCommissioningInfoAsync(ct);
        return new MatterCommissioningState(
            WindowStatus: await admin.ReadWindowStatusAsync(ct),
            AdminFabricIndex: await admin.ReadAdminFabricIndexAsync(ct),
            AdminVendorId: await admin.ReadAdminVendorIdAsync(ct),
            BasicCommissioningInfo: basicInfo is null
                ? null
                : new MatterBasicCommissioningInfo(
                    basicInfo.FailSafeExpiryLengthSeconds,
                    basicInfo.MaxCumulativeFailsafeSeconds),
            TCAcceptedVersion: await general.ReadTCAcceptedVersionAsync(ct),
            TCMinRequiredVersion: await general.ReadTCMinRequiredVersionAsync(ct),
            TCAcknowledgements: await general.ReadTCAcknowledgementsAsync(ct),
            TCAcknowledgementsRequired: await general.ReadTCAcknowledgementsRequiredAsync(ct),
            TCUpdateDeadline: await general.ReadTCUpdateDeadlineAsync(ct));
    }

    /// <summary>Read the current commissioning state through a resilient operational session.</summary>
    public static Task<MatterCommissioningState> ReadCommissioningStateAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadCommissioningStateAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Open a basic commissioning window on the target node.</summary>
    public static Task<InvokeResponse> OpenBasicCommissioningWindowAsync(
        ISession session,
        ushort commissioningTimeout,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new AdministratorCommissioningCluster(session, endpointId)
            .OpenBasicCommissioningWindowAsync(commissioningTimeout, ct);
    }

    /// <summary>Open a basic commissioning window through a resilient operational session.</summary>
    public static Task<InvokeResponse> OpenBasicCommissioningWindowAsync(
        ResilientSession session,
        ushort commissioningTimeout,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => OpenBasicCommissioningWindowAsync(secureSession, commissioningTimeout, endpointId, ct), ct);
    }

    /// <summary>Open an enhanced commissioning window on the target node.</summary>
    public static Task<InvokeResponse> OpenCommissioningWindowAsync(
        ISession session,
        MatterEnhancedCommissioningWindowParameters parameters,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parameters.PakePasscodeVerifier);
        ArgumentNullException.ThrowIfNull(parameters.Salt);

        return new AdministratorCommissioningCluster(session, endpointId).OpenCommissioningWindowAsync(
            commissioningTimeout: parameters.CommissioningTimeout,
            pAKEPasscodeVerifier: parameters.PakePasscodeVerifier,
            discriminator: parameters.Discriminator,
            iterations: parameters.Iterations,
            salt: parameters.Salt,
            ct: ct);
    }

    /// <summary>Open an enhanced commissioning window through a resilient operational session.</summary>
    public static Task<InvokeResponse> OpenCommissioningWindowAsync(
        ResilientSession session,
        MatterEnhancedCommissioningWindowParameters parameters,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => OpenCommissioningWindowAsync(secureSession, parameters, endpointId, ct), ct);
    }

    /// <summary>Revoke any open commissioning window on the target node.</summary>
    public static Task<InvokeResponse> RevokeCommissioningAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new AdministratorCommissioningCluster(session, endpointId).RevokeCommissioningAsync(ct);
    }

    /// <summary>Revoke any open commissioning window through a resilient operational session.</summary>
    public static Task<InvokeResponse> RevokeCommissioningAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RevokeCommissioningAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Send CommissioningComplete on the target node.</summary>
    public static Task<InvokeResponse> CompleteCommissioningAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new GeneralCommissioningCluster(session, endpointId).CommissioningCompleteAsync(ct);
    }

    /// <summary>Send CommissioningComplete through a resilient operational session.</summary>
    public static Task<InvokeResponse> CompleteCommissioningAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => CompleteCommissioningAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Read the current operational-credentials state from the root endpoint.</summary>
    public static async Task<MatterOperationalCredentialsState> ReadOperationalCredentialsAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cluster = new OperationalCredentialsCluster(session, endpointId);
        return new MatterOperationalCredentialsState(
            Fabrics: await cluster.ReadFabricsAsync(ct) ?? [],
            Nocs: await cluster.ReadNOCsAsync(ct) ?? [],
            TrustedRootCertificates: await cluster.ReadTrustedRootCertificatesAsync(ct) ?? [],
            SupportedFabrics: await cluster.ReadSupportedFabricsAsync(ct),
            CommissionedFabrics: await cluster.ReadCommissionedFabricsAsync(ct),
            CurrentFabricIndex: await cluster.ReadCurrentFabricIndexAsync(ct));
    }

    /// <summary>Read the current operational-credentials state through a resilient operational session.</summary>
    public static Task<MatterOperationalCredentialsState> ReadOperationalCredentialsAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadOperationalCredentialsAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Read Groups state from one application endpoint.</summary>
    public static async Task<MatterGroupsState> ReadGroupsStateAsync(
        ISession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cluster = new GroupsCluster(session, endpointId);
        return new MatterGroupsState(await cluster.ReadNameSupportAsync(ct));
    }

    /// <summary>Read Groups state through a resilient operational session.</summary>
    public static Task<MatterGroupsState> ReadGroupsStateAsync(
        ResilientSession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadGroupsStateAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Read Group Key Management state from the root endpoint.</summary>
    public static async Task<MatterGroupKeyManagementState> ReadGroupKeyManagementStateAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cluster = new GroupKeyManagementCluster(session, endpointId);
        return new MatterGroupKeyManagementState(
            GroupKeyMap: [.. (await cluster.ReadGroupKeyMapAsync(ct) ?? []).Select(static entry => new MatterGroupKeyMapEntry(
                entry.GroupId,
                entry.GroupKeySetID))],
            GroupTable: [.. (await cluster.ReadGroupTableAsync(ct) ?? []).Select(static entry => new MatterGroupTableEntry(
                entry.GroupId,
                entry.Endpoints ?? [],
                entry.GroupName))],
            MaxGroupsPerFabric: await cluster.ReadMaxGroupsPerFabricAsync(ct),
            MaxGroupKeysPerFabric: await cluster.ReadMaxGroupKeysPerFabricAsync(ct));
    }

    /// <summary>Read Group Key Management state through a resilient operational session.</summary>
    public static Task<MatterGroupKeyManagementState> ReadGroupKeyManagementStateAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadGroupKeyManagementStateAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Read Scenes Management state from one application endpoint.</summary>
    public static async Task<MatterScenesState> ReadScenesStateAsync(
        ISession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cluster = new ScenesManagementCluster(session, endpointId);
        return new MatterScenesState(
            SceneTableSize: await cluster.ReadSceneTableSizeAsync(ct),
            FabricSceneInfo: [.. (await cluster.ReadFabricSceneInfoAsync(ct) ?? []).Select(static scene => new MatterSceneInfo(
                scene.SceneCount,
                scene.CurrentScene,
                scene.CurrentGroup,
                scene.SceneValid,
                scene.RemainingCapacity,
                scene.FabricIndex))]);
    }

    /// <summary>Read Scenes Management state through a resilient operational session.</summary>
    public static Task<MatterScenesState> ReadScenesStateAsync(
        ResilientSession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadScenesStateAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Read the current Network Commissioning state from the root endpoint.</summary>
    public static async Task<MatterNetworkCommissioningState> ReadNetworkCommissioningStateAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cluster = new NetworkCommissioningCluster(session, endpointId);
        var features = await MatterInteractions.ReadNullableAttributeAsync<NetworkCommissioningCluster.Feature>(
            session,
            endpointId,
            NetworkCommissioningCluster.ClusterId,
            FeatureMapAttributeId,
            ct);
        var lastNetworkingStatus = await MatterInteractions.ReadNullableAttributeAsync<NetworkCommissioningCluster.NetworkCommissioningStatusEnum>(
            session,
            endpointId,
            NetworkCommissioningCluster.ClusterId,
            NetworkCommissioningCluster.Attributes.LastNetworkingStatus,
            ct);
        var supportedThreadFeatures = await MatterInteractions.ReadNullableAttributeAsync<NetworkCommissioningCluster.ThreadCapabilitiesBitmap>(
            session,
            endpointId,
            NetworkCommissioningCluster.ClusterId,
            NetworkCommissioningCluster.Attributes.SupportedThreadFeatures,
            ct);
        var threadVersion = await MatterInteractions.ReadNullableAttributeAsync<ushort>(
            session,
            endpointId,
            NetworkCommissioningCluster.ClusterId,
            NetworkCommissioningCluster.Attributes.ThreadVersion,
            ct);

        return new MatterNetworkCommissioningState(
            Features: features,
            Networks: [.. (await cluster.ReadNetworksAsync(ct) ?? []).Select(static network => new MatterNetworkCommissioningNetwork(
                network.NetworkID,
                network.Connected,
                network.NetworkIdentifier,
                network.ClientIdentifier))],
            MaxNetworks: await cluster.ReadMaxNetworksAsync(ct),
            ScanMaxTimeSeconds: await cluster.ReadScanMaxTimeSecondsAsync(ct),
            ConnectMaxTimeSeconds: await cluster.ReadConnectMaxTimeSecondsAsync(ct),
            InterfaceEnabled: await cluster.ReadInterfaceEnabledAsync(ct),
            LastNetworkingStatus: lastNetworkingStatus,
            LastNetworkId: await cluster.ReadLastNetworkIDAsync(ct),
            LastConnectErrorValue: await cluster.ReadLastConnectErrorValueAsync(ct),
            SupportedWiFiBands: await cluster.ReadSupportedWiFiBandsAsync(ct) ?? [],
            SupportedThreadFeatures: supportedThreadFeatures,
            ThreadVersion: threadVersion);
    }

    /// <summary>Read the current Network Commissioning state through a resilient operational session.</summary>
    public static Task<MatterNetworkCommissioningState> ReadNetworkCommissioningStateAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadNetworkCommissioningStateAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Scan available networks through the Network Commissioning cluster.</summary>
    public static async Task<MatterNetworkScanCommandResult> ScanNetworksAsync(
        ISession session,
        byte[]? ssid = null,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseScanNetworksResponse(
            await new NetworkCommissioningCluster(session, endpointId).ScanNetworksAsync(ssid, breadcrumb, ct));
    }

    /// <summary>Scan available networks through a resilient operational session.</summary>
    public static Task<MatterNetworkScanCommandResult> ScanNetworksAsync(
        ResilientSession session,
        byte[]? ssid = null,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => ScanNetworksAsync(secureSession, ssid, breadcrumb, endpointId, ct),
            ct);
    }

    /// <summary>Add or update Wi-Fi credentials through the Network Commissioning cluster.</summary>
    public static async Task<MatterNetworkConfigCommandResult> AddOrUpdateWiFiNetworkAsync(
        ISession session,
        byte[] ssid,
        byte[] credentials,
        ulong? breadcrumb = null,
        byte[]? networkIdentity = null,
        byte[]? clientIdentifier = null,
        byte[]? possessionNonce = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ssid);
        ArgumentNullException.ThrowIfNull(credentials);

        return ParseNetworkConfigResponse(
            await new NetworkCommissioningCluster(session, endpointId).AddOrUpdateWiFiNetworkAsync(
                ssid,
                credentials,
                breadcrumb,
                networkIdentity,
                clientIdentifier,
                possessionNonce,
                ct));
    }

    /// <summary>Add or update Wi-Fi credentials through a resilient operational session.</summary>
    public static Task<MatterNetworkConfigCommandResult> AddOrUpdateWiFiNetworkAsync(
        ResilientSession session,
        byte[] ssid,
        byte[] credentials,
        ulong? breadcrumb = null,
        byte[]? networkIdentity = null,
        byte[]? clientIdentifier = null,
        byte[]? possessionNonce = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => AddOrUpdateWiFiNetworkAsync(
                secureSession,
                ssid,
                credentials,
                breadcrumb,
                networkIdentity,
                clientIdentifier,
                possessionNonce,
                endpointId,
                ct),
            ct);
    }

    /// <summary>Add or update Thread credentials through the Network Commissioning cluster.</summary>
    public static async Task<MatterNetworkConfigCommandResult> AddOrUpdateThreadNetworkAsync(
        ISession session,
        byte[] operationalDataset,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(operationalDataset);

        return ParseNetworkConfigResponse(
            await new NetworkCommissioningCluster(session, endpointId).AddOrUpdateThreadNetworkAsync(
                operationalDataset,
                breadcrumb,
                ct));
    }

    /// <summary>Add or update Thread credentials through a resilient operational session.</summary>
    public static Task<MatterNetworkConfigCommandResult> AddOrUpdateThreadNetworkAsync(
        ResilientSession session,
        byte[] operationalDataset,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => AddOrUpdateThreadNetworkAsync(
                secureSession,
                operationalDataset,
                breadcrumb,
                endpointId,
                ct),
            ct);
    }

    /// <summary>Remove one configured network through the Network Commissioning cluster.</summary>
    public static async Task<MatterNetworkConfigCommandResult> RemoveNetworkAsync(
        ISession session,
        byte[] networkId,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(networkId);

        return ParseNetworkConfigResponse(
            await new NetworkCommissioningCluster(session, endpointId).RemoveNetworkAsync(networkId, breadcrumb, ct));
    }

    /// <summary>Remove one configured network through a resilient operational session.</summary>
    public static Task<MatterNetworkConfigCommandResult> RemoveNetworkAsync(
        ResilientSession session,
        byte[] networkId,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => RemoveNetworkAsync(secureSession, networkId, breadcrumb, endpointId, ct),
            ct);
    }

    /// <summary>Connect to one configured network through the Network Commissioning cluster.</summary>
    public static async Task<MatterConnectNetworkCommandResult> ConnectNetworkAsync(
        ISession session,
        byte[] networkId,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(networkId);

        return ParseConnectNetworkResponse(
            await new NetworkCommissioningCluster(session, endpointId).ConnectNetworkAsync(networkId, breadcrumb, ct));
    }

    /// <summary>Connect to one configured network through a resilient operational session.</summary>
    public static Task<MatterConnectNetworkCommandResult> ConnectNetworkAsync(
        ResilientSession session,
        byte[] networkId,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => ConnectNetworkAsync(secureSession, networkId, breadcrumb, endpointId, ct),
            ct);
    }

    /// <summary>Reorder one configured network through the Network Commissioning cluster.</summary>
    public static async Task<MatterNetworkConfigCommandResult> ReorderNetworkAsync(
        ISession session,
        byte[] networkId,
        byte networkIndex,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(networkId);

        return ParseNetworkConfigResponse(
            await new NetworkCommissioningCluster(session, endpointId).ReorderNetworkAsync(networkId, networkIndex, breadcrumb, ct));
    }

    /// <summary>Reorder one configured network through a resilient operational session.</summary>
    public static Task<MatterNetworkConfigCommandResult> ReorderNetworkAsync(
        ResilientSession session,
        byte[] networkId,
        byte networkIndex,
        ulong? breadcrumb = null,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => ReorderNetworkAsync(secureSession, networkId, networkIndex, breadcrumb, endpointId, ct),
            ct);
    }

    /// <summary>Write the InterfaceEnabled attribute through the Network Commissioning cluster.</summary>
    public static Task<WriteResponse> WriteNetworkInterfaceEnabledAsync(
        ISession session,
        bool interfaceEnabled,
        bool timedRequest = true,
        ushort timedTimeoutMs = 5000,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new NetworkCommissioningCluster(session, endpointId)
            .WriteInterfaceEnabledAsync(interfaceEnabled, timedRequest, timedTimeoutMs, ct);
    }

    /// <summary>Write the InterfaceEnabled attribute through a resilient operational session.</summary>
    public static Task<WriteResponse> WriteNetworkInterfaceEnabledAsync(
        ResilientSession session,
        bool interfaceEnabled,
        bool timedRequest = true,
        ushort timedTimeoutMs = 5000,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => WriteNetworkInterfaceEnabledAsync(
                secureSession,
                interfaceEnabled,
                timedRequest,
                timedTimeoutMs,
                endpointId,
                ct),
            ct);
    }

    /// <summary>Add one group to an application endpoint.</summary>
    public static async Task<MatterGroupCommandResult> AddGroupAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        string groupName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        return ParseGroupCommandResponse(
            await new GroupsCluster(session, endpointId).AddGroupAsync(groupId, groupName, ct),
            expectGroupName: false);
    }

    /// <summary>Add one group through a resilient operational session.</summary>
    public static Task<MatterGroupCommandResult> AddGroupAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        string groupName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => AddGroupAsync(secureSession, endpointId, groupId, groupName, ct), ct);
    }

    /// <summary>Read one group entry from an application endpoint.</summary>
    public static async Task<MatterGroupCommandResult> ViewGroupAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseGroupCommandResponse(
            await new GroupsCluster(session, endpointId).ViewGroupAsync(groupId, ct),
            expectGroupName: true);
    }

    /// <summary>Read one group entry through a resilient operational session.</summary>
    public static Task<MatterGroupCommandResult> ViewGroupAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ViewGroupAsync(secureSession, endpointId, groupId, ct), ct);
    }

    /// <summary>Read group membership for one application endpoint.</summary>
    public static async Task<MatterGroupMembershipCommandResult> GetGroupMembershipAsync(
        ISession session,
        ushort endpointId,
        IReadOnlyList<ushort>? groupIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseGroupMembershipResponse(
            await new GroupsCluster(session, endpointId).GetGroupMembershipAsync(groupIds?.ToArray() ?? [], ct));
    }

    /// <summary>Read group membership through a resilient operational session.</summary>
    public static Task<MatterGroupMembershipCommandResult> GetGroupMembershipAsync(
        ResilientSession session,
        ushort endpointId,
        IReadOnlyList<ushort>? groupIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => GetGroupMembershipAsync(secureSession, endpointId, groupIds, ct), ct);
    }

    /// <summary>Remove one group from an application endpoint.</summary>
    public static async Task<MatterGroupCommandResult> RemoveGroupAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseGroupCommandResponse(
            await new GroupsCluster(session, endpointId).RemoveGroupAsync(groupId, ct),
            expectGroupName: false);
    }

    /// <summary>Remove one group through a resilient operational session.</summary>
    public static Task<MatterGroupCommandResult> RemoveGroupAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveGroupAsync(secureSession, endpointId, groupId, ct), ct);
    }

    /// <summary>Remove all groups from an application endpoint.</summary>
    public static async Task<MatterInvokeCommandResult> RemoveAllGroupsAsync(
        ISession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseInvokeOnlyResponse(await new GroupsCluster(session, endpointId).RemoveAllGroupsAsync(ct));
    }

    /// <summary>Remove all groups through a resilient operational session.</summary>
    public static Task<MatterInvokeCommandResult> RemoveAllGroupsAsync(
        ResilientSession session,
        ushort endpointId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveAllGroupsAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Add one group only if the endpoint is identifying.</summary>
    public static async Task<MatterInvokeCommandResult> AddGroupIfIdentifyingAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        string groupName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        return ParseInvokeOnlyResponse(
            await new GroupsCluster(session, endpointId).AddGroupIfIdentifyingAsync(groupId, groupName, ct));
    }

    /// <summary>Add one group if identifying through a resilient operational session.</summary>
    public static Task<MatterInvokeCommandResult> AddGroupIfIdentifyingAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        string groupName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => AddGroupIfIdentifyingAsync(secureSession, endpointId, groupId, groupName, ct), ct);
    }

    /// <summary>Write one Group Key Set to the root endpoint.</summary>
    public static async Task<MatterInvokeCommandResult> WriteGroupKeySetAsync(
        ISession session,
        MatterGroupKeySet groupKeySet,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(groupKeySet);
        return ParseInvokeOnlyResponse(
            await new GroupKeyManagementCluster(session, endpointId).KeySetWriteAsync(ToGeneratedGroupKeySet(groupKeySet), ct));
    }

    /// <summary>Write one Group Key Set through a resilient operational session.</summary>
    public static Task<MatterInvokeCommandResult> WriteGroupKeySetAsync(
        ResilientSession session,
        MatterGroupKeySet groupKeySet,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => WriteGroupKeySetAsync(secureSession, groupKeySet, endpointId, ct), ct);
    }

    /// <summary>Read one Group Key Set from the root endpoint.</summary>
    public static async Task<MatterGroupKeySetReadCommandResult> ReadGroupKeySetAsync(
        ISession session,
        ushort groupKeySetId,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseGroupKeySetReadResponse(
            await new GroupKeyManagementCluster(session, endpointId).KeySetReadAsync(groupKeySetId, ct));
    }

    /// <summary>Read one Group Key Set through a resilient operational session.</summary>
    public static Task<MatterGroupKeySetReadCommandResult> ReadGroupKeySetAsync(
        ResilientSession session,
        ushort groupKeySetId,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadGroupKeySetAsync(secureSession, groupKeySetId, endpointId, ct), ct);
    }

    /// <summary>Remove one Group Key Set from the root endpoint.</summary>
    public static async Task<MatterInvokeCommandResult> RemoveGroupKeySetAsync(
        ISession session,
        ushort groupKeySetId,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseInvokeOnlyResponse(
            await new GroupKeyManagementCluster(session, endpointId).KeySetRemoveAsync(groupKeySetId, ct));
    }

    /// <summary>Remove one Group Key Set through a resilient operational session.</summary>
    public static Task<MatterInvokeCommandResult> RemoveGroupKeySetAsync(
        ResilientSession session,
        ushort groupKeySetId,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveGroupKeySetAsync(secureSession, groupKeySetId, endpointId, ct), ct);
    }

    /// <summary>Read all known Group Key Set identifiers from the root endpoint.</summary>
    public static async Task<MatterGroupKeySetReadAllIndicesCommandResult> ReadAllGroupKeySetIndicesAsync(
        ISession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseGroupKeySetReadAllIndicesResponse(
            await new GroupKeyManagementCluster(session, endpointId).KeySetReadAllIndicesAsync(ct));
    }

    /// <summary>Read all known Group Key Set identifiers through a resilient operational session.</summary>
    public static Task<MatterGroupKeySetReadAllIndicesCommandResult> ReadAllGroupKeySetIndicesAsync(
        ResilientSession session,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ReadAllGroupKeySetIndicesAsync(secureSession, endpointId, ct), ct);
    }

    /// <summary>Add one scene to an application endpoint.</summary>
    public static async Task<MatterSceneCommandResult> AddSceneAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        uint transitionTime,
        string sceneName,
        IReadOnlyList<MatterSceneExtensionFieldSet> extensionFieldSets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        ArgumentNullException.ThrowIfNull(extensionFieldSets);
        return ParseSceneCommandResponse(
            await new ScenesManagementCluster(session, endpointId).AddSceneAsync(
                groupId,
                sceneId,
                transitionTime,
                sceneName,
                [.. extensionFieldSets.Select(ToGeneratedSceneExtensionFieldSet)],
                ct));
    }

    /// <summary>Add one scene through a resilient operational session.</summary>
    public static Task<MatterSceneCommandResult> AddSceneAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        uint transitionTime,
        string sceneName,
        IReadOnlyList<MatterSceneExtensionFieldSet> extensionFieldSets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => AddSceneAsync(secureSession, endpointId, groupId, sceneId, transitionTime, sceneName, extensionFieldSets, ct),
            ct);
    }

    /// <summary>Read one scene from an application endpoint.</summary>
    public static async Task<MatterViewSceneCommandResult> ViewSceneAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseViewSceneResponse(
            await new ScenesManagementCluster(session, endpointId).ViewSceneAsync(groupId, sceneId, ct));
    }

    /// <summary>Read one scene through a resilient operational session.</summary>
    public static Task<MatterViewSceneCommandResult> ViewSceneAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => ViewSceneAsync(secureSession, endpointId, groupId, sceneId, ct), ct);
    }

    /// <summary>Remove one scene from an application endpoint.</summary>
    public static async Task<MatterSceneCommandResult> RemoveSceneAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseSceneCommandResponse(
            await new ScenesManagementCluster(session, endpointId).RemoveSceneAsync(groupId, sceneId, ct));
    }

    /// <summary>Remove one scene through a resilient operational session.</summary>
    public static Task<MatterSceneCommandResult> RemoveSceneAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveSceneAsync(secureSession, endpointId, groupId, sceneId, ct), ct);
    }

    /// <summary>Remove all scenes for one group from an application endpoint.</summary>
    public static async Task<MatterSceneCommandResult> RemoveAllScenesAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseSceneCommandResponse(
            await new ScenesManagementCluster(session, endpointId).RemoveAllScenesAsync(groupId, ct));
    }

    /// <summary>Remove all scenes through a resilient operational session.</summary>
    public static Task<MatterSceneCommandResult> RemoveAllScenesAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveAllScenesAsync(secureSession, endpointId, groupId, ct), ct);
    }

    /// <summary>Store one scene on an application endpoint.</summary>
    public static async Task<MatterSceneCommandResult> StoreSceneAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseSceneCommandResponse(
            await new ScenesManagementCluster(session, endpointId).StoreSceneAsync(groupId, sceneId, ct));
    }

    /// <summary>Store one scene through a resilient operational session.</summary>
    public static Task<MatterSceneCommandResult> StoreSceneAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => StoreSceneAsync(secureSession, endpointId, groupId, sceneId, ct), ct);
    }

    /// <summary>Recall one scene on an application endpoint.</summary>
    public static async Task<MatterInvokeCommandResult> RecallSceneAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        uint? transitionTime = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseInvokeOnlyResponse(
            await new ScenesManagementCluster(session, endpointId).RecallSceneAsync(groupId, sceneId, transitionTime, ct));
    }

    /// <summary>Recall one scene through a resilient operational session.</summary>
    public static Task<MatterInvokeCommandResult> RecallSceneAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        byte sceneId,
        uint? transitionTime = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RecallSceneAsync(secureSession, endpointId, groupId, sceneId, transitionTime, ct), ct);
    }

    /// <summary>Read scene membership for one group from an application endpoint.</summary>
    public static async Task<MatterSceneMembershipCommandResult> GetSceneMembershipAsync(
        ISession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ParseSceneMembershipResponse(
            await new ScenesManagementCluster(session, endpointId).GetSceneMembershipAsync(groupId, ct));
    }

    /// <summary>Read scene membership through a resilient operational session.</summary>
    public static Task<MatterSceneMembershipCommandResult> GetSceneMembershipAsync(
        ResilientSession session,
        ushort endpointId,
        ushort groupId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => GetSceneMembershipAsync(secureSession, endpointId, groupId, ct), ct);
    }

    /// <summary>Copy one scene or all scenes on an application endpoint.</summary>
    public static async Task<MatterSceneCopyCommandResult> CopySceneAsync(
        ISession session,
        ushort endpointId,
        bool copyAllScenes,
        ushort groupIdentifierFrom,
        byte sceneIdentifierFrom,
        ushort groupIdentifierTo,
        byte sceneIdentifierTo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var mode = copyAllScenes
            ? ScenesManagementCluster.CopyModeBitmap.CopyAllScenes
            : (ScenesManagementCluster.CopyModeBitmap)0;
        return ParseSceneCopyResponse(
            await new ScenesManagementCluster(session, endpointId).CopySceneAsync(
                mode,
                groupIdentifierFrom,
                sceneIdentifierFrom,
                groupIdentifierTo,
                sceneIdentifierTo,
                ct));
    }

    /// <summary>Copy one scene or all scenes through a resilient operational session.</summary>
    public static Task<MatterSceneCopyCommandResult> CopySceneAsync(
        ResilientSession session,
        ushort endpointId,
        bool copyAllScenes,
        ushort groupIdentifierFrom,
        byte sceneIdentifierFrom,
        ushort groupIdentifierTo,
        byte sceneIdentifierTo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(
            secureSession => CopySceneAsync(
                secureSession,
                endpointId,
                copyAllScenes,
                groupIdentifierFrom,
                sceneIdentifierFrom,
                groupIdentifierTo,
                sceneIdentifierTo,
                ct),
            ct);
    }

    /// <summary>Update the current fabric label on the target node.</summary>
    public static Task<InvokeResponse> UpdateFabricLabelAsync(
        ISession session,
        string label,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new OperationalCredentialsCluster(session, endpointId).UpdateFabricLabelAsync(label, ct);
    }

    /// <summary>Update the current fabric label through a resilient operational session.</summary>
    public static Task<InvokeResponse> UpdateFabricLabelAsync(
        ResilientSession session,
        string label,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => UpdateFabricLabelAsync(secureSession, label, endpointId, ct), ct);
    }

    /// <summary>Remove a fabric from the target node.</summary>
    public static Task<InvokeResponse> RemoveFabricAsync(
        ISession session,
        byte fabricIndex,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new OperationalCredentialsCluster(session, endpointId).RemoveFabricAsync(fabricIndex, ct);
    }

    /// <summary>Remove a fabric through a resilient operational session.</summary>
    public static Task<InvokeResponse> RemoveFabricAsync(
        ResilientSession session,
        byte fabricIndex,
        ushort endpointId = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UseSessionAsync(secureSession => RemoveFabricAsync(secureSession, fabricIndex, endpointId, ct), ct);
    }

    private static MatterTLV CreateResponseFieldsReader(MatterTLV responseFields)
    {
        var reader = new MatterTLV(responseFields.GetBytes());
        reader.OpenStructure(1);
        return reader;
    }

    private static MatterNetworkScanCommandResult ParseScanNetworksResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, [], []);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "ScanNetworks succeeded without response fields.", null, null, [], []);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (NetworkCommissioningCluster.NetworkCommissioningStatusEnum)reader.GetUnsignedInt8(0);
            string? debugText = reader.IsNextTag(1) ? reader.GetUTF8String(1) : null;

            var wifiResults = new List<MatterWiFiScanResult>();
            if (reader.IsNextTag(2))
            {
                reader.OpenArray(2);
                while (!reader.IsEndContainerNext())
                {
                    wifiResults.Add(ReadWiFiScanResult(reader));
                }

                reader.CloseContainer();
            }

            var threadResults = new List<MatterThreadScanResult>();
            if (reader.IsNextTag(3))
            {
                reader.OpenArray(3);
                while (!reader.IsEndContainerNext())
                {
                    threadResults.Add(ReadThreadScanResult(reader));
                }

                reader.CloseContainer();
            }

            return new(true, response.StatusCode, null, status, debugText, wifiResults, threadResults);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, [], []);
        }
    }

    private static MatterNetworkConfigCommandResult ParseNetworkConfigResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null, null, null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "Network command succeeded without response fields.", null, null, null, null, null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (NetworkCommissioningCluster.NetworkCommissioningStatusEnum)reader.GetUnsignedInt8(0);
            string? debugText = reader.IsNextTag(1) ? reader.GetUTF8String(1) : null;
            byte? networkIndex = reader.IsNextTag(2) ? reader.GetUnsignedInt8(2) : null;
            byte[]? clientIdentity = reader.IsNextTag(3) ? reader.GetOctetString(3) : null;
            byte[]? possessionSignature = reader.IsNextTag(4) ? reader.GetOctetString(4) : null;
            return new(true, response.StatusCode, null, status, debugText, networkIndex, clientIdentity, possessionSignature);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null, null, null);
        }
    }

    private static MatterConnectNetworkCommandResult ParseConnectNetworkResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "ConnectNetwork succeeded without response fields.", null, null, null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (NetworkCommissioningCluster.NetworkCommissioningStatusEnum)reader.GetUnsignedInt8(0);
            string? debugText = reader.IsNextTag(1) ? reader.GetUTF8String(1) : null;
            int? errorValue = null;
            if (reader.IsNextTag(2))
            {
                if (reader.IsNextNull())
                {
                    reader.GetNull(2);
                }
                else
                {
                    errorValue = checked((int)reader.GetSignedInt(2));
                }
            }

            return new(true, response.StatusCode, null, status, debugText, errorValue);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null);
        }
    }

    private static MatterInvokeCommandResult ParseInvokeOnlyResponse(InvokeResponse response)
        => response.Success
            ? new(true, response.StatusCode, null)
            : new(false, response.StatusCode, FormatInvokeFailure(response));

    private static MatterGroupCommandResult ParseGroupCommandResponse(InvokeResponse response, bool expectGroupName)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "Groups command succeeded without response fields.", null, null, null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (MatterStatusCode)reader.GetUnsignedInt8(0);
            ushort? groupId = reader.IsNextTag(1) ? (ushort)reader.GetUnsignedIntAny(1) : null;
            string? groupName = expectGroupName && reader.IsNextTag(2) ? reader.GetUTF8String(2) : null;
            return new(true, response.StatusCode, null, status, groupId, groupName);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null);
        }
    }

    private static MatterGroupMembershipCommandResult ParseGroupMembershipResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, []);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "GetGroupMembership succeeded without response fields.", null, []);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            byte? capacity = null;
            if (reader.IsNextTag(0))
            {
                if (reader.IsNextNull())
                {
                    reader.GetNull(0);
                }
                else
                {
                    capacity = reader.GetUnsignedInt8(0);
                }
            }

            var groupIds = new List<ushort>();
            if (reader.IsNextTag(1))
            {
                reader.OpenArray(1);
                while (!reader.IsEndContainerNext())
                {
                    groupIds.Add((ushort)reader.GetUnsignedInt(null));
                }

                reader.CloseContainer();
            }

            return new(true, response.StatusCode, null, capacity, groupIds);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, []);
        }
    }

    private static MatterGroupKeySetReadCommandResult ParseGroupKeySetReadResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "KeySetRead succeeded without response fields.", null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            return new(true, response.StatusCode, null, ReadGroupKeySet(reader, 0));
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null);
        }
    }

    private static MatterGroupKeySetReadAllIndicesCommandResult ParseGroupKeySetReadAllIndicesResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), []);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "KeySetReadAllIndices succeeded without response fields.", []);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var groupKeySetIds = new List<ushort>();
            if (reader.IsNextTag(0))
            {
                reader.OpenArray(0);
                while (!reader.IsEndContainerNext())
                {
                    groupKeySetIds.Add((ushort)reader.GetUnsignedInt(null));
                }

                reader.CloseContainer();
            }

            return new(true, response.StatusCode, null, groupKeySetIds);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, []);
        }
    }

    private static MatterSceneCommandResult ParseSceneCommandResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "Scenes command succeeded without response fields.", null, null, null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (MatterStatusCode)reader.GetUnsignedInt8(0);
            ushort? groupId = reader.IsNextTag(1) ? (ushort)reader.GetUnsignedIntAny(1) : null;
            byte? sceneId = reader.IsNextTag(2) ? reader.GetUnsignedInt8(2) : null;
            return new(true, response.StatusCode, null, status, groupId, sceneId);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null);
        }
    }

    private static MatterViewSceneCommandResult ParseViewSceneResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null, null, null, []);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "ViewScene succeeded without response fields.", null, null, null, null, null, []);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (MatterStatusCode)reader.GetUnsignedInt8(0);
            ushort? groupId = reader.IsNextTag(1) ? (ushort)reader.GetUnsignedIntAny(1) : null;
            byte? sceneId = reader.IsNextTag(2) ? reader.GetUnsignedInt8(2) : null;
            uint? transitionTime = reader.IsNextTag(3) ? reader.GetUnsignedIntAny(3) : null;
            string? sceneName = reader.IsNextTag(4) ? reader.GetUTF8String(4) : null;

            var extensionFieldSets = new List<MatterSceneExtensionFieldSet>();
            if (reader.IsNextTag(5))
            {
                reader.OpenArray(5);
                while (!reader.IsEndContainerNext())
                {
                    extensionFieldSets.Add(ReadSceneExtensionFieldSet(reader));
                }

                reader.CloseContainer();
            }

            return new(true, response.StatusCode, null, status, groupId, sceneId, transitionTime, sceneName, extensionFieldSets);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null, null, null, []);
        }
    }

    private static MatterSceneMembershipCommandResult ParseSceneMembershipResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null, []);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "GetSceneMembership succeeded without response fields.", null, null, null, []);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (MatterStatusCode)reader.GetUnsignedInt8(0);
            byte? capacity = null;
            if (reader.IsNextTag(1))
            {
                if (reader.IsNextNull())
                {
                    reader.GetNull(1);
                }
                else
                {
                    capacity = reader.GetUnsignedInt8(1);
                }
            }

            ushort? groupId = reader.IsNextTag(2) ? (ushort)reader.GetUnsignedIntAny(2) : null;
            var sceneIds = new List<byte>();
            if (reader.IsNextTag(3))
            {
                reader.OpenArray(3);
                while (!reader.IsEndContainerNext())
                {
                    sceneIds.Add((byte)reader.GetUnsignedInt(null));
                }

                reader.CloseContainer();
            }

            return new(true, response.StatusCode, null, status, capacity, groupId, sceneIds);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null, []);
        }
    }

    private static MatterSceneCopyCommandResult ParseSceneCopyResponse(InvokeResponse response)
    {
        if (!response.Success)
        {
            return new(false, response.StatusCode, FormatInvokeFailure(response), null, null, null);
        }

        if (response.ResponseFields is null)
        {
            return new(true, response.StatusCode, "CopyScene succeeded without response fields.", null, null, null);
        }

        try
        {
            var reader = CreateResponseFieldsReader(response.ResponseFields);
            var status = (MatterStatusCode)reader.GetUnsignedInt8(0);
            ushort? groupIdentifierFrom = reader.IsNextTag(1) ? (ushort)reader.GetUnsignedIntAny(1) : null;
            byte? sceneIdentifierFrom = reader.IsNextTag(2) ? reader.GetUnsignedInt8(2) : null;
            return new(true, response.StatusCode, null, status, groupIdentifierFrom, sceneIdentifierFrom);
        }
        catch (Exception ex)
        {
            return new(true, response.StatusCode, ex.Message, null, null, null);
        }
    }

    private static GroupKeyManagementCluster.GroupKeySetStruct ToGeneratedGroupKeySet(MatterGroupKeySet groupKeySet)
        => new()
        {
            GroupKeySetID = groupKeySet.GroupKeySetId,
            GroupKeySecurityPolicy = groupKeySet.GroupKeySecurityPolicy,
            EpochKey0 = groupKeySet.EpochKey0 ?? [],
            EpochStartTime0 = groupKeySet.EpochStartTime0,
            EpochKey1 = groupKeySet.EpochKey1 ?? [],
            EpochStartTime1 = groupKeySet.EpochStartTime1,
            EpochKey2 = groupKeySet.EpochKey2 ?? [],
            EpochStartTime2 = groupKeySet.EpochStartTime2,
        };

    private static ScenesManagementCluster.ExtensionFieldSetStruct ToGeneratedSceneExtensionFieldSet(MatterSceneExtensionFieldSet extensionFieldSet)
        => new()
        {
            ClusterID = extensionFieldSet.ClusterId,
            AttributeValueList = [.. extensionFieldSet.AttributeValues.Select(ToGeneratedSceneAttributeValue)],
        };

    private static ScenesManagementCluster.AttributeValuePairStruct ToGeneratedSceneAttributeValue(MatterSceneAttributeValue attributeValue)
        => new()
        {
            AttributeID = attributeValue.AttributeId,
            ValueUnsigned8 = attributeValue.ValueUnsigned8,
            ValueSigned8 = attributeValue.ValueSigned8,
            ValueUnsigned16 = attributeValue.ValueUnsigned16,
            ValueSigned16 = attributeValue.ValueSigned16,
            ValueUnsigned32 = attributeValue.ValueUnsigned32,
            ValueSigned32 = attributeValue.ValueSigned32,
            ValueUnsigned64 = attributeValue.ValueUnsigned64,
            ValueSigned64 = attributeValue.ValueSigned64,
        };

    private static MatterGroupKeySet ReadGroupKeySet(MatterTLV reader, int? tag = null)
    {
        ushort groupKeySetId = 0;
        var groupKeySecurityPolicy = GroupKeyManagementCluster.GroupKeySecurityPolicyEnum.TrustFirst;
        byte[]? epochKey0 = null;
        ulong? epochStartTime0 = null;
        byte[]? epochKey1 = null;
        ulong? epochStartTime1 = null;
        byte[]? epochKey2 = null;
        ulong? epochStartTime2 = null;

        reader.OpenStructure(tag);
        while (!reader.IsEndContainerNext())
        {
            switch (reader.PeekTag())
            {
                case 0:
                    groupKeySetId = (ushort)reader.GetUnsignedIntAny(0);
                    break;
                case 1:
                    groupKeySecurityPolicy = (GroupKeyManagementCluster.GroupKeySecurityPolicyEnum)reader.GetUnsignedIntAny(1);
                    break;
                case 2:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(2);
                    }
                    else
                    {
                        epochKey0 = reader.GetOctetString(2);
                    }
                    break;
                case 3:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(3);
                    }
                    else
                    {
                        epochStartTime0 = reader.GetUnsignedInt(3);
                    }
                    break;
                case 4:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(4);
                    }
                    else
                    {
                        epochKey1 = reader.GetOctetString(4);
                    }
                    break;
                case 5:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(5);
                    }
                    else
                    {
                        epochStartTime1 = reader.GetUnsignedInt(5);
                    }
                    break;
                case 6:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(6);
                    }
                    else
                    {
                        epochKey2 = reader.GetOctetString(6);
                    }
                    break;
                case 7:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(7);
                    }
                    else
                    {
                        epochStartTime2 = reader.GetUnsignedInt(7);
                    }
                    break;
                default:
                    reader.SkipElement();
                    break;
            }
        }

        reader.CloseContainer();
        return new MatterGroupKeySet(
            groupKeySetId,
            groupKeySecurityPolicy,
            epochKey0,
            epochStartTime0,
            epochKey1,
            epochStartTime1,
            epochKey2,
            epochStartTime2);
    }

    private static MatterSceneExtensionFieldSet ReadSceneExtensionFieldSet(MatterTLV reader)
    {
        uint clusterId = 0;
        var attributeValues = new List<MatterSceneAttributeValue>();

        reader.OpenStructure(null);
        while (!reader.IsEndContainerNext())
        {
            switch (reader.PeekTag())
            {
                case 0:
                    clusterId = reader.GetUnsignedIntAny(0);
                    break;
                case 1:
                    reader.OpenArray(1);
                    while (!reader.IsEndContainerNext())
                    {
                        attributeValues.Add(ReadSceneAttributeValue(reader));
                    }

                    reader.CloseContainer();
                    break;
                default:
                    reader.SkipElement();
                    break;
            }
        }

        reader.CloseContainer();
        return new MatterSceneExtensionFieldSet(clusterId, attributeValues);
    }

    private static MatterSceneAttributeValue ReadSceneAttributeValue(MatterTLV reader)
    {
        uint attributeId = 0;
        byte? valueUnsigned8 = null;
        sbyte? valueSigned8 = null;
        ushort? valueUnsigned16 = null;
        short? valueSigned16 = null;
        uint? valueUnsigned32 = null;
        int? valueSigned32 = null;
        ulong? valueUnsigned64 = null;
        long? valueSigned64 = null;

        reader.OpenStructure(null);
        while (!reader.IsEndContainerNext())
        {
            switch (reader.PeekTag())
            {
                case 0:
                    attributeId = reader.GetUnsignedIntAny(0);
                    break;
                case 1:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(1);
                    }
                    else
                    {
                        valueUnsigned8 = reader.GetUnsignedInt8(1);
                    }
                    break;
                case 2:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(2);
                    }
                    else
                    {
                        valueSigned8 = checked((sbyte)reader.GetSignedInt(2));
                    }
                    break;
                case 3:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(3);
                    }
                    else
                    {
                        valueUnsigned16 = (ushort)reader.GetUnsignedIntAny(3);
                    }
                    break;
                case 4:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(4);
                    }
                    else
                    {
                        valueSigned16 = checked((short)reader.GetSignedInt(4));
                    }
                    break;
                case 5:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(5);
                    }
                    else
                    {
                        valueUnsigned32 = reader.GetUnsignedIntAny(5);
                    }
                    break;
                case 6:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(6);
                    }
                    else
                    {
                        valueSigned32 = checked((int)reader.GetSignedInt(6));
                    }
                    break;
                case 7:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(7);
                    }
                    else
                    {
                        valueUnsigned64 = reader.GetUnsignedInt(7);
                    }
                    break;
                case 8:
                    if (reader.IsNextNull())
                    {
                        reader.GetNull(8);
                    }
                    else
                    {
                        valueSigned64 = reader.GetSignedInt(8);
                    }
                    break;
                default:
                    reader.SkipElement();
                    break;
            }
        }

        reader.CloseContainer();
        return new MatterSceneAttributeValue(
            attributeId,
            valueUnsigned8,
            valueSigned8,
            valueUnsigned16,
            valueSigned16,
            valueUnsigned32,
            valueSigned32,
            valueUnsigned64,
            valueSigned64);
    }

    private static MatterWiFiScanResult ReadWiFiScanResult(MatterTLV reader)
    {
        var result = new MatterWiFiScanResult(
            0,
            [],
            [],
            0,
            0,
            0);

        reader.OpenStructure(null);
        while (!reader.IsEndContainerNext())
        {
            switch (reader.PeekTag())
            {
                case 0:
                    result = result with
                    {
                        Security = (NetworkCommissioningCluster.WiFiSecurityBitmap)reader.GetUnsignedIntAny(0)
                    };
                    break;
                case 1:
                    result = result with
                    {
                        Ssid = reader.GetOctetString(1)
                    };
                    break;
                case 2:
                    result = result with
                    {
                        Bssid = reader.GetOctetString(2)
                    };
                    break;
                case 3:
                    result = result with
                    {
                        Channel = (ushort)reader.GetUnsignedIntAny(3)
                    };
                    break;
                case 4:
                    result = result with
                    {
                        WiFiBand = (NetworkCommissioningCluster.WiFiBandEnum)reader.GetUnsignedIntAny(4)
                    };
                    break;
                case 5:
                    result = result with
                    {
                        Rssi = checked((sbyte)reader.GetSignedInt(5))
                    };
                    break;
                default:
                    reader.SkipElement();
                    break;
            }
        }

        reader.CloseContainer();
        return result;
    }

    private static MatterThreadScanResult ReadThreadScanResult(MatterTLV reader)
    {
        var result = new MatterThreadScanResult(
            0,
            0,
            string.Empty,
            0,
            0,
            [],
            0,
            0);

        reader.OpenStructure(null);
        while (!reader.IsEndContainerNext())
        {
            switch (reader.PeekTag())
            {
                case 0:
                    result = result with
                    {
                        PanId = (ushort)reader.GetUnsignedIntAny(0)
                    };
                    break;
                case 1:
                    result = result with
                    {
                        ExtendedPanId = reader.GetUnsignedInt64(1)
                    };
                    break;
                case 2:
                    result = result with
                    {
                        NetworkName = reader.GetUTF8String(2)
                    };
                    break;
                case 3:
                    result = result with
                    {
                        Channel = (ushort)reader.GetUnsignedIntAny(3)
                    };
                    break;
                case 4:
                    result = result with
                    {
                        Version = reader.GetUnsignedInt8(4)
                    };
                    break;
                case 5:
                    result = result with
                    {
                        ExtendedAddress = reader.GetOctetString(5)
                    };
                    break;
                case 6:
                    result = result with
                    {
                        Rssi = checked((sbyte)reader.GetSignedInt(6))
                    };
                    break;
                case 7:
                    result = result with
                    {
                        Lqi = reader.GetUnsignedInt8(7)
                    };
                    break;
                default:
                    reader.SkipElement();
                    break;
            }
        }

        reader.CloseContainer();
        return result;
    }

    internal static string FormatInteractionModelStatus(byte statusCode)
        => Enum.IsDefined(typeof(MatterStatusCode), statusCode)
            ? $"status={(MatterStatusCode)statusCode} (0x{statusCode:X2})"
            : $"status=0x{statusCode:X2}";

    private static string FormatInvokeFailure(InvokeResponse response)
        => FormatInteractionModelStatus(response.StatusCode)
            + (string.IsNullOrWhiteSpace(response.Error) ? string.Empty : $" ({response.Error})");
}
