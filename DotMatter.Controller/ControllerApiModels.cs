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
/// <summary>Result of a WiFi commissioning operation.</summary>
public record WifiCommissioningResult(bool Success, string? DeviceId, string? NodeId, string? OperationalIp, string? Error);
/// <summary>Current state of a Matter device.</summary>
public record DeviceState(bool? OnOff, bool IsOnline, DateTime? LastSeen, byte? Level, byte? Hue, byte? Saturation,
    ushort? ColorX, ushort? ColorY, byte? ColorMode, string? VendorName, string? ProductName);
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
