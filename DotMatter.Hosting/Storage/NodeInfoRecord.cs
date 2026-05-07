using System.Text.Json.Serialization;

namespace DotMatter.Hosting;

/// <summary>
/// Stored node information persisted to node_info.json in each fabric directory.
/// </summary>
public sealed record NodeInfoRecord(
    string NodeId,
    string? ThreadIPv6,
    string FabricName,
    string? DeviceName = null,
    string? Transport = null,
    DateTime? Commissioned = null,
    string? VendorName = null,
    string? ProductName = null,
    string? DeviceType = null,
    ushort? ColorCapabilities = null,
    Dictionary<ushort, List<uint>>? Endpoints = null,
    ushort? OperationalPort = null);

/// <summary>AOT-safe JSON context for DotMatter.Hosting types.</summary>
[JsonSerializable(typeof(NodeInfoRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
public partial class HostingJsonContext : JsonSerializerContext;

/// <summary>Indented variant for human-readable file persistence.</summary>
[JsonSerializable(typeof(NodeInfoRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = true)]
public partial class HostingJsonIndentedContext : JsonSerializerContext;
