using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinViet.Api.IntegrationTests.Infrastructure;

/// <summary>One HTTP response, parsed leniently into a <see cref="JsonNode"/>.</summary>
public sealed record ApiResult(HttpStatusCode Status, JsonNode? Json, string Raw)
{
    public int Code => (int)Status;
    public bool Success => Json?["success"]?.GetValue<bool>() ?? false;
    public string? Message => Json?["message"]?.GetValue<string>();
}

public static class HttpClientJsonExtensions
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async Task<ApiResult> SendAsync(
        this ApiTestFixture fx,
        HttpMethod method,
        string path,
        object? body = null,
        string? token = null,
        IDictionary<string, string>? headers = null)
        => await fx.Client.SendJsonAsync(method, path, body, token, headers);

    public static async Task<ApiResult> SendJsonAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? token = null,
        IDictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headers is not null)
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        JsonNode? node = null;
        try { node = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); } catch { /* non-JSON body */ }
        return new ApiResult(res.StatusCode, node, raw);
    }

    /// <summary>Multipart file upload (used by the CSV import endpoint).</summary>
    public static async Task<ApiResult> UploadFileAsync(
        this HttpClient client,
        string path,
        string fieldName,
        string fileName,
        byte[] content,
        string contentType,
        string? token = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, fieldName, fileName);

        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        JsonNode? node = null;
        try { node = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); } catch { }
        return new ApiResult(res.StatusCode, node, raw);
    }
}
