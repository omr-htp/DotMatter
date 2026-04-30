using System.Text.Json.Serialization;

namespace DotMatter.Controller;

[JsonSerializable(typeof(CommissionRequest))]
[JsonSerializable(typeof(WifiCommissionRequest))]
[JsonSerializable(typeof(ControllerCommissioningResult))]
[JsonSerializable(typeof(WifiCommissioningResult))]
[JsonSerializable(typeof(DeviceSummary))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(LevelRequest))]
[JsonSerializable(typeof(ColorRequest))]
[JsonSerializable(typeof(ColorXYRequest))]
[JsonSerializable(typeof(SwitchBindingRequest))]
[JsonSerializable(typeof(DeviceSummary[]))]
[JsonSerializable(typeof(IEnumerable<DeviceSummary>))]
[JsonSerializable(typeof(DeviceState))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(DeviceCounts))]
[JsonSerializable(typeof(ReadinessResponse))]
[JsonSerializable(typeof(LivenessResponse))]
[JsonSerializable(typeof(DeviceEvent))]
[JsonSerializable(typeof(CommissionEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ControllerJsonContext : JsonSerializerContext;
