using System.Text.Json;

namespace DotMatter.Controller;

/// <summary>Commission a new Matter device via BLE.</summary>
public class CommissionRequest
{
    /// <summary>BLE discriminator (e.g. 3840)</summary>
    public int Discriminator { get; set; }
    /// <summary>Device passcode (default: 20202021)</summary>
    public uint Passcode { get; set; } = 20202021;
    /// <summary>Optional fabric/device name</summary>
    public string? FabricName { get; set; }
    /// <summary>Manual pairing code (e.g. "34970112332"). If provided, discriminator and passcode are extracted automatically.</summary>
    public string? ManualCode { get; set; }
    /// <summary>QR code payload (e.g. "MT:Y.K9042C00KA0648G00"). If provided, discriminator and passcode are extracted automatically.</summary>
    public string? QrCode { get; set; }
}

/// <summary>Commission a WiFi Matter device via BLE.</summary>
public class WifiCommissionRequest
{
    /// <summary>BLE discriminator (e.g. 3840)</summary>
    public int Discriminator { get; set; }
    /// <summary>Device passcode (default: 20202021)</summary>
    public uint Passcode { get; set; } = 20202021;
    /// <summary>Optional fabric/device name</summary>
    public string? FabricName { get; set; }
    /// <summary>WiFi SSID the device should join</summary>
    public string WifiSsid { get; set; } = "";
    /// <summary>WiFi password</summary>
    public string WifiPassword { get; set; } = "";
    /// <summary>Manual pairing code. If provided, discriminator and passcode are extracted automatically.</summary>
    public string? ManualCode { get; set; }
    /// <summary>QR code payload. If provided, discriminator and passcode are extracted automatically.</summary>
    public string? QrCode { get; set; }
}

/// <summary>Summary of a Matter device.</summary>
public record DeviceSummary(string Id, string Name, string NodeId, string Ip, int Port, bool IsOnline, bool? OnOff, byte? Level, byte? Hue, byte? Saturation, DateTime? LastSeen);
/// <summary>Detailed information about a Matter device.</summary>
public record DeviceDetail(string Id, string Name, string NodeId, string Ip, int Port, bool IsOnline, bool? OnOff, byte? Level, byte? Hue, byte? Saturation, byte? ColorMode, DateTime? LastSeen, string? VendorName, string? ProductName);
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
    string FollowUpConnectTimeout);
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
