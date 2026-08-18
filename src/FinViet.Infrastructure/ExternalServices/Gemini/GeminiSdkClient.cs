using System.Text;
using Google.GenAI;
using Google.GenAI.Types;

namespace FinViet.Infrastructure.ExternalServices.Gemini;

internal sealed record GeminiGenerationResult(
    string? Text,
    string? ModelVersion = null,
    string? ResponseId = null,
    int? PromptTokenCount = null,
    int? CandidatesTokenCount = null,
    int? TotalTokenCount = null);

internal sealed record GeminiEmbeddingResult(
    double[]? Values,
    double? TokenCount = null,
    int? BillableCharacterCount = null);

internal interface IGeminiSdkClient
{
    Task<GeminiGenerationResult> GenerateContentAsync(
        string model,
        string prompt,
        GenerateContentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Multimodal overload — <paramref name="content"/> carries the image/text parts
    /// directly (see <see cref="Part.FromBytes"/>/<see cref="Part.FromText"/>).</summary>
    Task<GeminiGenerationResult> GenerateContentAsync(
        string model,
        Content content,
        GenerateContentConfig config,
        CancellationToken cancellationToken = default);

    Task<GeminiEmbeddingResult> EmbedContentAsync(
        string model,
        string text,
        int outputDimensions,
        CancellationToken cancellationToken = default);
}

/// <summary>Keeps official SDK response types behind a small testable provider boundary.</summary>
internal sealed class GeminiSdkClient : IGeminiSdkClient
{
    private readonly Client _client;

    public GeminiSdkClient(Client client)
    {
        _client = client;
    }

    public async Task<GeminiGenerationResult> GenerateContentAsync(
        string model,
        string prompt,
        GenerateContentConfig config,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Models.GenerateContentAsync(
            model: model,
            contents: prompt,
            config: config,
            cancellationToken: cancellationToken);

        return new GeminiGenerationResult(
            ExtractAnswerText(response.Parts),
            response.ModelVersion,
            response.ResponseId,
            response.UsageMetadata?.PromptTokenCount,
            response.UsageMetadata?.CandidatesTokenCount,
            response.UsageMetadata?.TotalTokenCount);
    }

    public async Task<GeminiGenerationResult> GenerateContentAsync(
        string model,
        Content content,
        GenerateContentConfig config,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Models.GenerateContentAsync(
            model: model,
            contents: content,
            config: config,
            cancellationToken: cancellationToken);

        return new GeminiGenerationResult(
            ExtractAnswerText(response.Parts),
            response.ModelVersion,
            response.ResponseId,
            response.UsageMetadata?.PromptTokenCount,
            response.UsageMetadata?.CandidatesTokenCount,
            response.UsageMetadata?.TotalTokenCount);
    }

    internal static string? ExtractAnswerText(IEnumerable<Part>? parts)
    {
        if (parts is null)
            return null;

        StringBuilder? answer = null;
        foreach (var part in parts)
        {
            if (part.Thought is true || part.Text is null)
                continue;

            answer ??= new StringBuilder();
            answer.Append(part.Text);
        }

        var text = answer?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public async Task<GeminiEmbeddingResult> EmbedContentAsync(
        string model,
        string text,
        int outputDimensions,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Models.EmbedContentAsync(
            model: model,
            contents: text,
            config: new EmbedContentConfig
            {
                OutputDimensionality = outputDimensions
            },
            cancellationToken: cancellationToken);

        var embedding = response.Embeddings?.FirstOrDefault();
        return new GeminiEmbeddingResult(
            embedding?.Values?.ToArray(),
            embedding?.Statistics?.TokenCount,
            response.Metadata?.BillableCharacterCount);
    }
}
