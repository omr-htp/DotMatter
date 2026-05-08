using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotMatter.Ui.Models;
using Microsoft.Extensions.Options;

namespace DotMatter.Ui.Services;

internal sealed class ControllerApiClient(
    HttpClient httpClient,
    IOptions<ControllerApiOptions> options)
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly ControllerApiOptions _options = options.Value;

    public string BaseUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? _options.BaseUrl.TrimEnd('/');

    public async Task<ApiCallResult> SendAsync(
        ApiOperationMethod method,
        string path,
        JsonNode? body = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(ToHttpMethod(method), path.TrimStart('/'));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyApiKeyHeader(request);

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(_serializerOptions), Encoding.UTF8, "application/json");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var raw = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var json = TryParseJson(raw);
            var error = response.IsSuccessStatusCode
                ? null
                : ExtractError(json, raw) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";

            return new ApiCallResult(
                response.IsSuccessStatusCode,
                response.StatusCode,
                error,
                NormalizeJson(raw, json),
                json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiCallResult(false, HttpStatusCode.RequestTimeout, "The request timed out.", null, null);
        }
        catch (Exception ex)
        {
            return new ApiCallResult(false, HttpStatusCode.ServiceUnavailable, ex.Message, null, null);
        }
    }

    public async Task<ApiCallResult<T>> GetAsync<T>(string path, CancellationToken ct = default)
        where T : class
    {
        var raw = await SendAsync(ApiOperationMethod.Get, path, ct: ct);
        if (!raw.Success || raw.RawContent is null)
        {
            return new ApiCallResult<T>(raw.Success, raw.StatusCode, raw.ErrorMessage, raw.RawContent, raw.Json, null);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(raw.RawContent, _serializerOptions);
            return new ApiCallResult<T>(value is not null, raw.StatusCode, value is null ? "Response body was empty." : null, raw.RawContent, raw.Json, value);
        }
        catch (Exception ex)
        {
            return new ApiCallResult<T>(false, raw.StatusCode, ex.Message, raw.RawContent, raw.Json, null);
        }
    }

    public Task<ApiCallResult<JsonNode>> GetJsonAsync(string path, CancellationToken ct = default)
        => GetAsync<JsonNode>(path, ct);

    public async Task<ApiCallResult<T>> PostAsync<T>(
        string path,
        JsonNode? body = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where T : class
    {
        var raw = await SendAsync(ApiOperationMethod.Post, path, body, timeout, ct);
        if (!raw.Success || raw.RawContent is null)
        {
            return new ApiCallResult<T>(raw.Success, raw.StatusCode, raw.ErrorMessage, raw.RawContent, raw.Json, null);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(raw.RawContent, _serializerOptions);
            return new ApiCallResult<T>(value is not null, raw.StatusCode, value is null ? "Response body was empty." : null, raw.RawContent, raw.Json, value);
        }
        catch (Exception ex)
        {
            return new ApiCallResult<T>(false, raw.StatusCode, ex.Message, raw.RawContent, raw.Json, null);
        }
    }

    public async Task StreamAsync(string path, Action<string> onMessage, CancellationToken ct = default)
        => await StreamAsync(path, (message, _) =>
        {
            onMessage(message);
            return ValueTask.CompletedTask;
        }, ct);

    public async Task StreamAsync(string path, Func<string, CancellationToken, ValueTask> onMessage, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path.TrimStart('/'));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        ApplyApiKeyHeader(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(ExtractError(TryParseJson(errorBody), errorBody) ?? $"Stream request failed with {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var builder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (builder.Length > 0)
                {
                    await onMessage(builder.ToString().TrimEnd(), ct);
                    builder.Clear();
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine(line[5..].TrimStart());
            }
        }
    }

    public static string PrettyPrint(string? rawContent)
    {
        var json = TryParseJson(rawContent);
        return NormalizeJson(rawContent, json) ?? string.Empty;
    }

    private static HttpMethod ToHttpMethod(ApiOperationMethod method)
        => method switch
        {
            ApiOperationMethod.Get => HttpMethod.Get,
            ApiOperationMethod.Post => HttpMethod.Post,
            ApiOperationMethod.Delete => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };

    private void ApplyApiKeyHeader(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return;
        }

        request.Headers.Remove(_options.ApiKeyHeaderName);
        request.Headers.Add(_options.ApiKeyHeaderName, _options.ApiKey);
    }

    private static JsonNode? TryParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeJson(string? raw, JsonNode? json)
        => json is not null
            ? json.ToJsonString(_serializerOptions)
            : raw;

    private static string? ExtractError(JsonNode? json, string? raw)
        => json?["error"]?.GetValue<string>()
           ?? json?["message"]?.GetValue<string>()
           ?? raw;
}
