namespace DotMatter.Ui.Models;

internal sealed record DeviceSummaryModel(
    string Id,
    string Name,
    string NodeId,
    string Ip,
    int Port,
    bool IsOnline,
    bool? OnOff,
    byte? Level,
    byte? Hue,
    byte? Saturation,
    DateTime? LastSeen,
    string? DeviceType);

internal sealed record DeviceDetailModel(
    string Id,
    string Name,
    string NodeId,
    string Ip,
    int Port,
    bool IsOnline,
    bool? OnOff,
    byte? Level,
    byte? Hue,
    byte? Saturation,
    byte? ColorMode,
    DateTime? LastSeen,
    string? VendorName,
    string? ProductName);

internal sealed record DeviceCapabilityDeviceTypeModel(
    uint DeviceType,
    string DeviceTypeHex,
    ushort Revision);

internal sealed record DeviceCapabilityClusterModel(
    uint ClusterId,
    string ClusterHex,
    string? ClusterName,
    bool SupportsEvents);

internal sealed record DeviceCapabilityEndpointModel(
    ushort Endpoint,
    string EndpointHex,
    IReadOnlyList<DeviceCapabilityDeviceTypeModel> DeviceTypes,
    IReadOnlyList<DeviceCapabilityClusterModel> ServerClusters,
    IReadOnlyList<DeviceCapabilityClusterModel> ClientClusters,
    IReadOnlyList<ushort> PartsList,
    string? UniqueId);

internal sealed record DeviceOperationSupportModel(
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

internal sealed record DeviceCapabilitySnapshotModel(
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
    IReadOnlyList<DeviceCapabilityEndpointModel> Endpoints,
    DeviceOperationSupportModel SupportedOperations);

internal sealed record DiscoveryDeviceModel(
    string Transport,
    string InstanceName,
    string FullyQualifiedName,
    IReadOnlyList<string> Addresses,
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
    int Port,
    IReadOnlyDictionary<string, string>? TxtRecords);

internal sealed record DiscoveryBrowseModel(
    int BrowseWindowMs,
    int TotalDiscovered,
    int MatchedCount,
    int ReturnedCount,
    IReadOnlyList<DiscoveryDeviceModel> Devices);

internal sealed record DiscoveryResolveModel(
    string Discriminator,
    int BrowseWindowMs,
    DiscoveryDeviceModel Device);
