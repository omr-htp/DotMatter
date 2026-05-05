namespace DotMatter.Ui.Services;

internal sealed class ControllerApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";
    public string? ApiKey
    {
        get; set;
    }
    public int RequestTimeoutSeconds { get; set; } = 45;
    public int StreamBufferLimit { get; set; } = 160;
}
