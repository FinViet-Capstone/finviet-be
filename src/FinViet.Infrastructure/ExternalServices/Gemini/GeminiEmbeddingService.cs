using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>
/// REST wrapper over the Gemini embedContent endpoint. Produces a single embedding vector
/// for a text input. Any transport, status, or parse failure surfaces as
/// <see cref="GeminiUnavailableException"/> so callers can fall back gracefully.
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    public GeminiEmbeddingService(
        HttpClient http,
        IOptions<GeminiOptions> options,
        ILogger<GeminiEmbeddingService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new GeminiUnavailableException("Gemini API key is not configured.");
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiUnavailableException("Cannot embed empty text.");

        var model = _options.EmbeddingModel;
        var body = new
        {
            model = $"models/{model}",
            content = new { parts = new[] { new { text } } },
            outputDimensionality = _options.EmbeddingDimensions
        };

        var url = $"{_options.BaseUrl.TrimEnd('/')}/models/{model}:embedContent?key={_options.ApiKey}";

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, body, JsonOpts, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Gemini embed request failed (transport/timeout).");
            throw new GeminiUnavailableException("Gemini embed request failed (transport or timeout).", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Gemini embed returned {Status}: {Body}", (int)response.StatusCode, err);
            throw new GeminiUnavailableException($"Gemini embed returned HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var values = ParseEmbedding(payload);

        if (values is null || values.Length == 0)
            throw new GeminiUnavailableException("Gemini returned an empty embedding.");

        return values;
    }

    /// <summary>Pull embedding.values from the embedContent response envelope.</summary>
    private static float[]? ParseEmbedding(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var values = doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values");

            var result = new float[values.GetArrayLength()];
            var i = 0;
            foreach (var v in values.EnumerateArray())
                result[i++] = v.GetSingle();

            return result;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
