namespace DotMatter.Ui.Models;

public enum ApiOperationMethod
{
    Get,
    Post,
    Delete,
}

public enum ApiFieldLocation
{
    Path,
    Query,
    Body,
}

public enum ApiFieldInputKind
{
    Text,
    UnsignedInteger,
    Integer,
    Boolean,
    Json,
}

public enum DeviceOperationCapability
{
    Always,
    OnOff,
    Level,
    ColorHueSaturation,
    ColorXy,
    NetworkCommissioning,
    Groups,
    Scenes,
    GroupKeys,
    AccessControl,
    Binding,
    SwitchBinding,
    MatterEvents,
}

public sealed record ApiFieldDefinition(
    string Name,
    string Label,
    ApiFieldLocation Location,
    ApiFieldInputKind Kind = ApiFieldInputKind.Text,
    bool Required = false,
    string? DefaultValue = null,
    bool DefaultBool = false,
    string? Placeholder = null,
    string? HelpText = null);

public sealed record ApiOperationDefinition(
    string Key,
    string Title,
    string Description,
    ApiOperationMethod Method,
    string PathTemplate,
    IReadOnlyList<ApiFieldDefinition>? Fields = null,
    string? ButtonText = null,
    bool LongRunning = false,
    DeviceOperationCapability RequiredCapability = DeviceOperationCapability.Always);
