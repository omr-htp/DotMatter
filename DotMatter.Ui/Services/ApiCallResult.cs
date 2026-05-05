using System.Net;
using System.Text.Json.Nodes;

namespace DotMatter.Ui.Services;

internal sealed record ApiCallResult(
    bool Success,
    HttpStatusCode StatusCode,
    string? ErrorMessage,
    string? RawContent,
    JsonNode? Json)
{
    public int StatusCodeValue => (int)StatusCode;
}

internal sealed record ApiCallResult<T>(
    bool Success,
    HttpStatusCode StatusCode,
    string? ErrorMessage,
    string? RawContent,
    JsonNode? Json,
    T? Value) where T : class
{
    public int StatusCodeValue => (int)StatusCode;
}
