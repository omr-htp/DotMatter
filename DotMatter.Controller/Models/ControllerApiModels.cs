using System.Text.Json;

namespace DotMatter.Controller.Models;

/// <summary>Commission a new Matter device via BLE.</summary>
public class CommissionRequest
{
    /// <summary>BLE discriminator (e.g. 3840)</summary>
    public int Discriminator
    {
        get; set;
    }
    /// <summary>Device passcode (default: 20202021)</summary>
    public uint Passcode { get; set; } = 20202021;
    /// <summary>Optional fabric/device name</summary>
    public string? FabricName
    {
        get; set;
    }
    /// <summary>Manual pairing code (e.g. "34970112332"). If provided, discriminator and passcode are extracted automatically.</summary>
    public string? ManualCode
    {
        get; set;
    }
    /// <summary>QR code payload (e.g. "MT:Y.K9042C00KA0648G00"). If provided, discriminator and passcode are extracted automatically.</summary>
    public string? QrCode
    {
        get; set;
    }
}

/// <summary>Commission a WiFi Matter device via BLE.</summary>
public class WifiCommissionRequest
{
    /// <summary>BLE discriminator (e.g. 3840)</summary>
    public int Discriminator
    {
        get; set;
    }
    /// <summary>Device passcode (default: 20202021)</summary>
    public uint Passcode { get; set; } = 20202021;
    /// <summary>Optional fabric/device name</summary>
    public string? FabricName
    {
        get; set;
    }
    /// <summary>WiFi SSID the device should join</summary>
    public string WifiSsid { get; set; } = "";
    /// <summary>WiFi password</summary>
    public string WifiPassword { get; set; } = "";
    /// <summary>Manual pairing code. If provided, discriminator and passcode are extracted automatically.</summary>
    public string? ManualCode
    {
        get; set;
    }
    /// <summary>QR code payload. If provided, discriminator and passcode are extracted automatically.</summary>
    public string? QrCode
    {
        get; set;
    }
}

/// <summary>Parsed browse request for commissionable-device discovery.</summary>
public record CommissionableDeviceDiscoveryRequest(
    TimeSpan Timeout,
    CommissionableDiscoveryTransport Transport = CommissionableDiscoveryTransport.All,
    ushort? Discriminator = null,
    ushort? VendorId = null,
    ushort? ProductId = null,
    uint? DeviceType = null,
    byte? CommissioningMode = null,
    string? DeviceNameContains = null,
    string? InstanceNameContains = null,
    string? RotatingIdentifierContains = null,
    int? Limit = null,
    bool IncludeTxtRecords = false);

/// <summary>Parsed resolve request for one commissionable device discriminator.</summary>
public record CommissionableDeviceResolveRequest(
    ushort Discriminator,
    TimeSpan Timeout,
    CommissionableDiscoveryTransport Transport = CommissionableDiscoveryTransport.All,
    bool IncludeTxtRecords = false);

/// <summary>Discovery transport(s) used when browsing commissionable devices.</summary>
public enum CommissionableDiscoveryTransport
{
    /// <summary>Use both supported browse transports.</summary>
    All = 0,
    /// <summary>Use local-network mDNS `_matterc._udp.local` browsing.</summary>
    Mdns = 1,
    /// <summary>Use Linux BlueZ BLE advertisement scanning.</summary>
    Ble = 2,
}

/// <summary>One commissionable device currently advertising over mDNS or BLE.</summary>
public record CommissionableDeviceResponse(
    string Transport,
    string InstanceName,
    string FullyQualifiedName,
    int Port,
    string[] Addresses,
    string? PreferredAddress,
    string? BluetoothAddress,
    short? Rssi,
    ushort? LongDiscriminator,
    string? LongDiscriminatorHex,
    ushort? ShortDiscriminator,
    string? ShortDiscriminatorHex,
    ushort? VendorId,
    string? VendorIdHex,
    ushort? ProductId,
    string? ProductIdHex,
    uint? DeviceType,
    string? DeviceTypeHex,
    string? DeviceName,
    byte? CommissioningMode,
    ushort? PairingHint,
    string? PairingInstruction,
    string? RotatingIdentifier,
    byte? AdvertisementVersion,
    string? ServiceDataHex,
    Dictionary<string, string>? TxtRecords);

/// <summary>Commissionable-device browse result.</summary>
public record CommissionableDeviceBrowseResponse(
    int BrowseWindowMs,
    int TotalDiscovered,
    int MatchedCount,
    int ReturnedCount,
    CommissionableDeviceResponse[] Devices);

/// <summary>Resolve result for one commissionable-device discriminator.</summary>
public record CommissionableDeviceResolveResponse(
    string Discriminator,
    int BrowseWindowMs,
    CommissionableDeviceResponse Device);

/// <summary>Summary of a Matter device.</summary>
public record DeviceSummary(string Id, string Name, string NodeId, string Ip, int Port, bool IsOnline, bool? OnOff, byte? Level, byte? Hue, byte? Saturation, DateTime? LastSeen, string? DeviceType);
/// <summary>Detailed information about a Matter device.</summary>
public record DeviceDetail(string Id, string Name, string NodeId, string Ip, int Port, bool IsOnline, bool? OnOff, byte? Level, byte? Hue, byte? Saturation, byte? ColorMode, DateTime? LastSeen, string? VendorName, string? ProductName);
/// <summary>One device type entry exposed to the UI from the Descriptor cluster.</summary>
public record DeviceCapabilityDeviceType(
    uint DeviceType,
    string DeviceTypeHex,
    ushort Revision);
/// <summary>One server/client cluster entry exposed to the UI.</summary>
public record DeviceCapabilityCluster(
    uint ClusterId,
    string ClusterHex,
    string? ClusterName,
    bool SupportsEvents);
/// <summary>Capability and topology snapshot for one endpoint.</summary>
public record DeviceCapabilityEndpoint(
    ushort Endpoint,
    string EndpointHex,
    DeviceCapabilityDeviceType[] DeviceTypes,
    DeviceCapabilityCluster[] ServerClusters,
    DeviceCapabilityCluster[] ClientClusters,
    ushort[] PartsList,
    string? UniqueId);
/// <summary>Derived UI-facing operation support flags for one selected device.</summary>
public record DeviceOperationSupport(
    bool OnOff,
    bool Level,
    bool ColorHueSaturation,
    bool ColorXy,
    bool NetworkCommissioning,
    bool Groups,
    bool Scenes,
    bool GroupKeys,
    bool AccessControl,
    bool Binding,
    bool SwitchBinding,
    bool MatterEvents);
/// <summary>Capability snapshot exposed to the Blazor device workbench.</summary>
public record DeviceCapabilitySnapshot(
    string SourceDeviceId,
    string? SourceDeviceName,
    bool IsOnline,
    string TopologySource,
    string? ControllerDeviceType,
    bool SupportsHueSaturation,
    bool SupportsXy,
    ushort? DataModelRevision,
    string? VendorName,
    ushort? VendorId,
    string? VendorIdHex,
    string? ProductName,
    ushort? ProductId,
    string? ProductIdHex,
    string? NodeLabel,
    DeviceCapabilityEndpoint[] Endpoints,
    DeviceOperationSupport SupportedOperations);
/// <summary>Result of a device command.</summary>
public record CommandResult(string Result);
/// <summary>Generic message response.</summary>
public record MessageResponse(string Message);
/// <summary>Request to set device brightness level.</summary>
public record LevelRequest(byte Level, ushort TransitionTime = 5);
/// <summary>Request to set device hue and saturation color.</summary>
public record ColorRequest(byte Hue, byte Saturation, ushort TransitionTime = 5);
/// <summary>Request to set device CIE XY color.</summary>
public record ColorXYRequest(ushort X, ushort Y, ushort TransitionTime = 5);
/// <summary>Request to bind a switch OnOff client to a target OnOff server.</summary>
public record SwitchBindingRequest(string TargetDeviceId, ushort SourceEndpoint = 1, ushort TargetEndpoint = 1);
/// <summary>Summary of one removal attempt.</summary>
public record RemovalStatus(string Outcome, int RemovedCount, int RemainingCount, string? Reason = null);
/// <summary>Request to remove Binding entries from a source endpoint.</summary>
public record DeviceBindingRemovalRequest(ushort Endpoint = 1, string? NodeId = null, ushort? Group = null, ushort? TargetEndpoint = null, uint? Cluster = null);
/// <summary>Request target selector for ACL entry removal.</summary>
public record DeviceAclRemovalTarget(uint? Cluster, ushort? Endpoint, uint? DeviceType);
/// <summary>Request to remove ACL entries from endpoint 0 of a device.</summary>
public record DeviceAclRemovalRequest(string Privilege, string AuthMode, string[]? Subjects = null, DeviceAclRemovalTarget[]? Targets = null, string? AuxiliaryType = null);
/// <summary>Result of removing Binding entries from one source device endpoint.</summary>
public record DeviceBindingRemovalResponse(string SourceDeviceId, string? SourceDeviceName, string SourceFabricName, ushort Endpoint, RemovalStatus Result, DeviceBindingEntry[] RemovedEntries, string? Error = null);
/// <summary>Result of removing ACL entries from one target device endpoint.</summary>
public record DeviceAclRemovalResponse(string SourceDeviceId, string? SourceDeviceName, string SourceFabricName, ushort Endpoint, RemovalStatus Result, DeviceAclEntry[] RemovedEntries, string? Error = null);
/// <summary>Result of removing a switch OnOff route and its matching target ACL grant.</summary>
public record SwitchBindingRemovalResponse(string SourceDeviceId, string? SourceDeviceName, string TargetDeviceId, string? TargetDeviceName, ushort SourceEndpoint, ushort TargetEndpoint, RemovalStatus Binding, RemovalStatus Acl, string? Error = null);
/// <summary>One ACL subject entry as seen in an AccessControl ACL entry.</summary>
public record DeviceAclSubject(string Value, string? DeviceId, string? DeviceName);
/// <summary>One ACL target entry as seen in an AccessControl ACL entry.</summary>
public record DeviceAclTarget(uint? Cluster, string? ClusterHex, ushort? Endpoint, uint? DeviceType, string? DeviceTypeHex);
/// <summary>One AccessControl ACL entry as observed from a device.</summary>
public record DeviceAclEntry(string Privilege, string AuthMode, DeviceAclSubject[]? Subjects, DeviceAclTarget[]? Targets, string? AuxiliaryType, byte FabricIndex);
/// <summary>ACL entries read from one source device endpoint.</summary>
public record DeviceAclListResponse(string SourceDeviceId, string? SourceDeviceName, string SourceFabricName, ushort Endpoint, DeviceAclEntry[] Entries, string? Error = null);
/// <summary>ACL entries aggregated across devices on a controller fabric.</summary>
public record FabricAclListResponse(string FabricName, int TotalSources, int SuccessfulSources, int FailedSources, DeviceAclListResponse[] Sources);
/// <summary>Single Binding cluster target entry as observed from a source device endpoint.</summary>
public record DeviceBindingEntry(string? NodeId, ushort? Group, ushort? Endpoint, uint? Cluster, string? ClusterHex, byte FabricIndex, string? TargetDeviceId, string? TargetDeviceName);
/// <summary>Binding entries read from one source device endpoint.</summary>
public record DeviceBindingListResponse(string SourceDeviceId, string? SourceDeviceName, string SourceFabricName, ushort Endpoint, DeviceBindingEntry[] Entries, string? Error = null);
/// <summary>Binding entries aggregated across devices on a controller fabric.</summary>
public record FabricBindingListResponse(string FabricName, int TotalSources, int SuccessfulSources, int FailedSources, DeviceBindingListResponse[] Sources);
/// <summary>Result of a WiFi commissioning operation.</summary>
public record WifiCommissioningResult(bool Success, string? DeviceId, string? NodeId, string? OperationalIp, string? Error);
/// <summary>Current state of a Matter device.</summary>
public record DeviceState(bool? OnOff, bool IsOnline, DateTime? LastSeen, byte? Level, byte? Hue, byte? Saturation,
    ushort? ColorX, ushort? ColorY, byte? ColorMode, string? VendorName, string? ProductName);
/// <summary>Request to open a basic commissioning window.</summary>
public record BasicCommissioningWindowRequest(ushort CommissioningTimeout = 180);
/// <summary>Request to open an enhanced commissioning window.</summary>
public record EnhancedCommissioningWindowRequest(
    ushort CommissioningTimeout = 180,
    string PakePasscodeVerifierHex = "",
    ushort Discriminator = 3840,
    uint Iterations = 1000,
    string SaltHex = "");
/// <summary>Request to update a fabric label on a device.</summary>
public record FabricLabelRequest(string Label);
/// <summary>Stable basic commissioning info response.</summary>
public record DeviceBasicCommissioningInfo(ushort FailSafeExpiryLengthSeconds, ushort MaxCumulativeFailsafeSeconds);
/// <summary>Device commissioning state as exposed by the controller API.</summary>
public record DeviceCommissioningState(
    string WindowStatus,
    byte? AdminFabricIndex,
    ushort? AdminVendorId,
    DeviceBasicCommissioningInfo? BasicCommissioningInfo,
    ushort TCAcceptedVersion,
    ushort TCMinRequiredVersion,
    ushort TCAcknowledgements,
    bool TCAcknowledgementsRequired,
    uint? TCUpdateDeadline);
/// <summary>Commissioning state read from one device.</summary>
public record DeviceCommissioningStateResponse(string SourceDeviceId, string? SourceDeviceName, DeviceCommissioningState State, string? Error = null);
/// <summary>One operational fabric descriptor as exposed by the controller API.</summary>
public record DeviceFabricDescriptor(
    string RootPublicKeyHex,
    ushort VendorId,
    ulong FabricId,
    ulong NodeId,
    string Label,
    string? VidVerificationStatementHex,
    byte FabricIndex);
/// <summary>One operational certificate entry as exposed by the controller API.</summary>
public record DeviceNocDescriptor(
    string NocHex,
    string? IcacHex,
    string? VvscHex,
    byte FabricIndex);
/// <summary>Operational credentials snapshot read from one device.</summary>
public record DeviceOperationalCredentialsResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    byte SupportedFabrics,
    byte CommissionedFabrics,
    byte CurrentFabricIndex,
    DeviceFabricDescriptor[] Fabrics,
    DeviceNocDescriptor[] Nocs,
    string[] TrustedRootCertificateHex,
    string? Error = null);
/// <summary>One network entry reported by Network Commissioning.</summary>
public record DeviceNetworkCommissioningNetwork(
    string NetworkIdHex,
    string? NetworkIdText,
    bool Connected,
    string? NetworkIdentifierHex,
    string? NetworkIdentifierText,
    string? ClientIdentifierHex);
/// <summary>Stable Network Commissioning state as exposed by the controller API.</summary>
public record DeviceNetworkCommissioningState(
    string[] Features,
    byte MaxNetworks,
    byte ScanMaxTimeSeconds,
    byte ConnectMaxTimeSeconds,
    bool InterfaceEnabled,
    string[] SupportedWiFiBands,
    string[] SupportedThreadFeatures,
    ushort? ThreadVersion,
    string? LastNetworkingStatus,
    string? LastNetworkIdHex,
    string? LastNetworkIdText,
    int? LastConnectErrorValue,
    DeviceNetworkCommissioningNetwork[] Networks);
/// <summary>Network Commissioning state read from one device.</summary>
public record DeviceNetworkCommissioningStateResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    DeviceNetworkCommissioningState State,
    string? Error = null);
/// <summary>Request to scan visible networks through Network Commissioning.</summary>
public record NetworkCommissioningScanRequest(string? Ssid = null, ulong? Breadcrumb = null);
/// <summary>Request to add or update one Wi-Fi network through Network Commissioning.</summary>
public record NetworkCommissioningWiFiRequest(
    string Ssid,
    string Credentials = "",
    ulong? Breadcrumb = null,
    string? NetworkIdentityHex = null,
    string? ClientIdentifierHex = null,
    string? PossessionNonceHex = null);
/// <summary>Request to add or update one Thread network through Network Commissioning.</summary>
public record NetworkCommissioningThreadRequest(string OperationalDatasetHex, ulong? Breadcrumb = null);
/// <summary>Request that targets one network identifier through Network Commissioning.</summary>
public record NetworkCommissioningNetworkIdRequest(string NetworkIdHex, ulong? Breadcrumb = null);
/// <summary>Request to reorder one network through Network Commissioning.</summary>
public record NetworkCommissioningReorderRequest(string NetworkIdHex, byte NetworkIndex, ulong? Breadcrumb = null);
/// <summary>Request to enable or disable the Network Commissioning interface.</summary>
public record NetworkCommissioningInterfaceEnabledRequest(bool InterfaceEnabled);
/// <summary>One Wi-Fi scan result exposed by the controller API.</summary>
public record DeviceNetworkCommissioningWiFiScanResult(
    string SsidHex,
    string? SsidText,
    string BssidHex,
    ushort Channel,
    string WiFiBand,
    sbyte Rssi,
    string[] Security);
/// <summary>One Thread scan result exposed by the controller API.</summary>
public record DeviceNetworkCommissioningThreadScanResult(
    ushort PanId,
    string ExtendedPanIdHex,
    string NetworkName,
    ushort Channel,
    byte Version,
    string ExtendedAddressHex,
    sbyte Rssi,
    byte Lqi);
/// <summary>Result of a Network Commissioning scan command.</summary>
public record DeviceNetworkCommissioningScanResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    bool InvokeSucceeded,
    bool Accepted,
    string? InvokeStatus,
    string? InvokeStatusHex,
    string? NetworkingStatus,
    string? DebugText,
    DeviceNetworkCommissioningWiFiScanResult[] WiFiScanResults,
    DeviceNetworkCommissioningThreadScanResult[] ThreadScanResults,
    string? Error = null);
/// <summary>Result of a Network Commissioning config-style command.</summary>
public record DeviceNetworkCommissioningCommandResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    bool InvokeSucceeded,
    bool Accepted,
    string? InvokeStatus,
    string? InvokeStatusHex,
    string? NetworkingStatus,
    string? DebugText,
    byte? NetworkIndex,
    string? ClientIdentityHex,
    string? PossessionSignatureHex,
    string? Error = null);
/// <summary>Result of a Network Commissioning connect command.</summary>
public record DeviceNetworkCommissioningConnectResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    bool InvokeSucceeded,
    bool Accepted,
    string? InvokeStatus,
    string? InvokeStatusHex,
    string? NetworkingStatus,
    string? DebugText,
    int? ErrorValue,
    string? Error = null);
/// <summary>One attribute write status exposed by the controller API.</summary>
public record DeviceAttributeWriteStatusResponse(
    uint AttributeId,
    string AttributeHex,
    byte StatusCode,
    string StatusHex,
    ushort? EndpointId,
    uint? ClusterId,
    string? ClusterHex,
    byte? ClusterStatusCode,
    string? ClusterStatusHex);
/// <summary>Result of writing the Network Commissioning interface-enabled attribute.</summary>
public record DeviceNetworkInterfaceWriteResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    bool Success,
    string? StatusHex,
    DeviceAttributeWriteStatusResponse[] AttributeStatuses,
    string? Error = null);
/// <summary>One group membership entry exposed by the controller API.</summary>
public record DeviceGroupMembershipEntry(
    ushort GroupId,
    string GroupIdHex,
    string? GroupName,
    ushort? GroupKeySetId,
    string? GroupKeySetIdHex);
/// <summary>Stable Groups state as exposed by the controller API.</summary>
public record DeviceGroupsState(
    ushort Endpoint,
    string[] NameSupport,
    DeviceGroupMembershipEntry[] Groups);
/// <summary>Groups state read from one device endpoint.</summary>
public record DeviceGroupsStateResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    DeviceGroupsState State,
    string? Error = null);
/// <summary>One Group Key Management map entry exposed by the controller API.</summary>
public record DeviceGroupKeyMapEntry(
    ushort GroupId,
    string GroupIdHex,
    ushort GroupKeySetId,
    string GroupKeySetIdHex);
/// <summary>One Group Key Management table entry exposed by the controller API.</summary>
public record DeviceGroupTableEntry(
    ushort GroupId,
    string GroupIdHex,
    ushort[] Endpoints,
    string? GroupName);
/// <summary>Stable Group Key Management state as exposed by the controller API.</summary>
public record DeviceGroupKeyManagementState(
    ushort Endpoint,
    DeviceGroupKeyMapEntry[] GroupKeyMap,
    DeviceGroupTableEntry[] GroupTable,
    ushort MaxGroupsPerFabric,
    ushort MaxGroupKeysPerFabric);
/// <summary>Group Key Management state read from one device.</summary>
public record DeviceGroupKeyManagementStateResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    DeviceGroupKeyManagementState State,
    string? Error = null);
/// <summary>One fabric-scoped scene info entry exposed by the controller API.</summary>
public record DeviceSceneInfo(
    byte SceneCount,
    byte CurrentScene,
    ushort CurrentGroup,
    bool SceneValid,
    byte RemainingCapacity,
    byte FabricIndex);
/// <summary>Stable Scenes Management state as exposed by the controller API.</summary>
public record DeviceScenesState(
    ushort Endpoint,
    ushort SceneTableSize,
    DeviceSceneInfo[] FabricSceneInfo);
/// <summary>Scenes state read from one device endpoint.</summary>
public record DeviceScenesStateResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    DeviceScenesState State,
    string? Error = null);
/// <summary>Request that targets one endpoint-scoped cluster operation.</summary>
public record EndpointScopedRequest(ushort Endpoint);
/// <summary>Request to add one group to an endpoint.</summary>
public record GroupAddRequest(ushort Endpoint, ushort GroupId, string GroupName);
/// <summary>Request that targets one group on an endpoint.</summary>
public record GroupCommandRequest(ushort Endpoint, ushort GroupId);
/// <summary>Request to query endpoint group membership.</summary>
public record GroupMembershipRequest(ushort Endpoint, ushort[]? GroupIds = null);
/// <summary>Result of a group command with a typed group/status payload.</summary>
public record DeviceGroupCommandResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Status,
    ushort? GroupId,
    string? GroupIdHex,
    string? GroupName,
    string? Error = null);
/// <summary>Result of a group membership command.</summary>
public record DeviceGroupMembershipResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    byte? Capacity,
    ushort[] GroupIds,
    string? Error = null);
/// <summary>Result of an invoke-only group/scene/key command.</summary>
public record DeviceInvokeOnlyCommandResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Error = null);
/// <summary>Request to write one group key set.</summary>
public record GroupKeySetWriteRequest(
    ushort GroupKeySetId,
    string GroupKeySecurityPolicy,
    string EpochKey0Hex,
    ulong EpochStartTime0,
    string? EpochKey1Hex = null,
    ulong? EpochStartTime1 = null,
    string? EpochKey2Hex = null,
    ulong? EpochStartTime2 = null);
/// <summary>Request that targets one group key set.</summary>
public record GroupKeySetIdRequest(ushort GroupKeySetId);
/// <summary>One group key set exposed by the controller API.</summary>
public record DeviceGroupKeySet(
    ushort GroupKeySetId,
    string GroupKeySetIdHex,
    string GroupKeySecurityPolicy,
    string? EpochKey0Hex,
    ulong? EpochStartTime0,
    string? EpochKey1Hex,
    ulong? EpochStartTime1,
    string? EpochKey2Hex,
    ulong? EpochStartTime2);
/// <summary>One group key set identifier exposed by the controller API.</summary>
public record DeviceGroupKeySetIndex(
    ushort GroupKeySetId,
    string GroupKeySetIdHex);
/// <summary>Result of reading one group key set.</summary>
public record DeviceGroupKeySetReadResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    DeviceGroupKeySet? GroupKeySet,
    string? Error = null);
/// <summary>Result of reading all group key set identifiers.</summary>
public record DeviceGroupKeySetIndicesResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    DeviceGroupKeySetIndex[] GroupKeySetIds,
    string? Error = null);
/// <summary>One typed scene attribute-value request entry.</summary>
public record SceneAttributeValueRequest(
    uint AttributeId,
    byte? ValueUnsigned8 = null,
    sbyte? ValueSigned8 = null,
    ushort? ValueUnsigned16 = null,
    short? ValueSigned16 = null,
    uint? ValueUnsigned32 = null,
    int? ValueSigned32 = null,
    ulong? ValueUnsigned64 = null,
    long? ValueSigned64 = null);
/// <summary>One typed scene extension-field-set request.</summary>
public record SceneExtensionFieldSetRequest(
    uint ClusterId,
    SceneAttributeValueRequest[] AttributeValues);
/// <summary>Request to add one scene to an endpoint.</summary>
public record SceneAddRequest(
    ushort Endpoint,
    ushort GroupId,
    byte SceneId,
    uint TransitionTime,
    string SceneName,
    SceneExtensionFieldSetRequest[] ExtensionFieldSets);
/// <summary>Request that targets one scene on an endpoint.</summary>
public record SceneCommandRequest(
    ushort Endpoint,
    ushort GroupId,
    byte SceneId);
/// <summary>Request that targets one group-scoped scene collection on an endpoint.</summary>
public record SceneGroupRequest(
    ushort Endpoint,
    ushort GroupId);
/// <summary>Request to recall one scene on an endpoint.</summary>
public record SceneRecallRequest(
    ushort Endpoint,
    ushort GroupId,
    byte SceneId,
    uint? TransitionTime = null);
/// <summary>Request to copy one scene or all scenes on an endpoint.</summary>
public record SceneCopyRequest(
    ushort Endpoint,
    bool CopyAllScenes,
    ushort GroupIdentifierFrom,
    byte SceneIdentifierFrom,
    ushort GroupIdentifierTo,
    byte SceneIdentifierTo);
/// <summary>One scene attribute-value entry exposed by the controller API.</summary>
public record DeviceSceneAttributeValue(
    uint AttributeId,
    string AttributeIdHex,
    byte? ValueUnsigned8 = null,
    sbyte? ValueSigned8 = null,
    ushort? ValueUnsigned16 = null,
    short? ValueSigned16 = null,
    uint? ValueUnsigned32 = null,
    int? ValueSigned32 = null,
    ulong? ValueUnsigned64 = null,
    long? ValueSigned64 = null);
/// <summary>One scene extension-field set exposed by the controller API.</summary>
public record DeviceSceneExtensionFieldSet(
    uint ClusterId,
    string ClusterIdHex,
    DeviceSceneAttributeValue[] AttributeValues);
/// <summary>Result of a scene command with a typed status payload.</summary>
public record DeviceSceneCommandResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Status,
    ushort? GroupId,
    string? GroupIdHex,
    byte? SceneId,
    string? Error = null);
/// <summary>Result of a ViewScene command.</summary>
public record DeviceSceneViewResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Status,
    ushort? GroupId,
    string? GroupIdHex,
    byte? SceneId,
    uint? TransitionTime,
    string? SceneName,
    DeviceSceneExtensionFieldSet[] ExtensionFieldSets,
    string? Error = null);
/// <summary>Result of a GetSceneMembership command.</summary>
public record DeviceSceneMembershipResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Status,
    byte? Capacity,
    ushort? GroupId,
    string? GroupIdHex,
    int[] SceneIds,
    string? Error = null);
/// <summary>Result of a CopyScene command.</summary>
public record DeviceSceneCopyResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    bool InvokeSucceeded,
    bool Accepted,
    string? Status,
    ushort? GroupIdentifierFrom,
    string? GroupIdentifierFromHex,
    byte? SceneIdentifierFrom,
    string? Error = null);
/// <summary>In-process controller diagnostic counters since startup.</summary>
public record RuntimeDiagnosticsCounters(
    long CommissioningAttempts,
    long CommissioningRejections,
    long ApiAuthenticationFailures,
    long RateLimitRejections,
    long ManagedReconnectRequests,
    long SubscriptionRestarts,
    long RegistryPersistenceFailures);
/// <summary>Safe runtime snapshot for the controller service.</summary>
public record RuntimeSnapshotResponse(
    string Status,
    string Environment,
    bool StartupCompleted,
    bool Ready,
    bool Stopping,
    string Uptime,
    DateTime StartedAtUtc,
    DeviceCounts Devices,
    RuntimeDiagnosticsCounters Counters,
    DateTime Timestamp,
    string? LastStartupError = null);
/// <summary>Non-secret API/runtime configuration summary for diagnostics.</summary>
public record RuntimeApiDiagnostics(
    bool RequireApiKey,
    string HeaderName,
    int AllowedCorsOriginCount,
    int RateLimitPermitLimit,
    string RateLimitWindow,
    int RateLimitQueueLimit,
    int SseClientBufferCapacity,
    string CommandTimeout,
    bool OpenApiEnabled);
/// <summary>Diagnostics gating and controller configuration summary.</summary>
public record RuntimeDetailedDiagnostics(
    bool DetailedEndpointEnabled,
    bool SensitiveDiagnosticsEnabled,
    int MaxRenderedBytes,
    string SharedFabricName,
    string DefaultFabricNamePrefix,
    string FollowUpConnectTimeout,
    string RegulatoryLocation,
    string RegulatoryCountryCode,
    string AttestationPolicy);
/// <summary>Detailed runtime diagnostics payload, gated by configuration.</summary>
public record RuntimeDetailedResponse(
    RuntimeSnapshotResponse Runtime,
    RuntimeApiDiagnostics Api,
    RuntimeDetailedDiagnostics Diagnostics);
/// <summary>Stable typed/unknown payload envelope for controller Matter event APIs.</summary>
public record MatterEventPayloadResponse(
    string Kind,
    JsonElement? Data = null,
    string? Reason = null);
/// <summary>Single Matter event envelope used by controller testing APIs.</summary>
public record MatterEventResponse(
    string DeviceId,
    string? DeviceName,
    ushort Endpoint,
    uint Cluster,
    string ClusterHex,
    string? ClusterName,
    uint EventId,
    string EventHex,
    string EventName,
    ulong EventNumber,
    byte Priority,
    ulong? EpochTimestamp,
    ulong? SystemTimestamp,
    ulong? DeltaEpochTimestamp,
    ulong? DeltaSystemTimestamp,
    MatterEventPayloadResponse Payload,
    string? PayloadTlvHex,
    byte? StatusCode,
    DateTime ReceivedAtUtc);
/// <summary>One-shot raw Matter event read response for a device and cluster.</summary>
public record DeviceMatterEventReadResponse(
    string SourceDeviceId,
    string? SourceDeviceName,
    ushort Endpoint,
    uint Cluster,
    string ClusterHex,
    uint? RequestedEventId,
    string? RequestedEventHex,
    MatterEventResponse[] Events,
    string? Error = null);
/// <summary>Error response payload.</summary>
public record ErrorResponse(string Error);
/// <summary>Count of devices by online status.</summary>
public record DeviceCounts(int Total, int Online);
/// <summary>Health check response.</summary>
public record HealthResponse(string Status, string Uptime, bool Ready, DeviceCounts Devices, DateTime Timestamp, string? LastError = null);
/// <summary>Readiness probe response.</summary>
public record ReadinessResponse(string Status, bool Ready, string? Error, DateTime Timestamp);
/// <summary>Liveness probe response.</summary>
public record LivenessResponse(string Status, DateTime Timestamp);
/// <summary>Event emitted when a device state changes.</summary>
public record DeviceEvent(string Device, string Type, string Value, DateTime Time);
/// <summary>Event emitted during commissioning.</summary>
public record CommissionEvent(string Source, string Type, string Value, DateTime Time);
