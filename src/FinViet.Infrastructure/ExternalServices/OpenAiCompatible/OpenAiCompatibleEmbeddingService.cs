using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.OpenAiCompatible;

/// <summary>Creates fixed-size embeddings through an OpenAI-compatible /embeddings endpoint.</summary>
public class OpenAiCompatibleEmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleEmbeddingService> _logger;

    public OpenAiCompatibleEmbeddingService(
        HttpClient http,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleEmbeddingService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderUnavailableException("Cannot embed empty text.");

        var body = new { model = _options.EmbeddingModel, input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        OpenAiCompatibleHttp.AddAuthorization(request, _options.ApiKey);

        var payload = await OpenAiCompatibleHttp.SendAsync(
            _http,
            request,
            _logger,
            "embedding request",
            cancellationToken);
        var values = ParseEmbedding(payload);
        if (values is null || values.Length == 0)
            throw new AiProviderUnavailableException("The embedding provider returned an empty embedding.");
        if (values.Length != AiOptions.RequiredEmbeddingDimensions)
        {
            throw new AiProviderUnavailableException(
                $"Embedding dimension {values.Length} does not match the required dimension {AiOptions.RequiredEmbeddingDimensions}.");
        }

        return values;
    }

    private static float[]? ParseEmbedding(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var values = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding");

            var result = new float[values.GetArrayLength()];
            var i = 0;
            foreach (var value in values.EnumerateArray())
                result[i++] = value.GetSingle();

            return result;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or
                                   IndexOutOfRangeException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
