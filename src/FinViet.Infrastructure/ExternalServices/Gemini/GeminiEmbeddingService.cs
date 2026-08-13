using System.Diagnostics;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>Creates 768-dimensional text embeddings with the official Google Gen AI SDK.</summary>
public sealed class GeminiEmbeddingService : IEmbeddingService
{
    private readonly IGeminiSdkClient _client;
    private readonly GeminiOptions _options;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly IAiTelemetryRecorder _telemetry;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    internal GeminiEmbeddingService(
        IGeminiSdkClient client,
        IOptions<GeminiOptions> options,
        IAiRateLimiter rateLimiter,
        IAiTelemetryRecorder telemetry,
        ILogger<GeminiEmbeddingService> logger)
    {
        _client = client;
        _options = options.Value;
        _rateLimiter = rateLimiter;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default,
        AiRequestContext? requestContext = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderUnavailableException("Cannot embed empty text.");

        var context = requestContext ?? new AiRequestContext("embedding");
        if (context.CustomerId is Guid customerId
            && !await _rateLimiter.TryAcquireAsync(customerId, context.Feature, cancellationToken))
        {
            await _telemetry.RecordUsageAsync(
                new AiUsageRecord(
                    context.Feature,
                    "gemini",
                    "rate_limited",
                    customerId,
                    context.SessionId,
                    _options.EmbeddingModel),
                cancellationToken);
            throw new AiProviderUnavailableException("Gemini embedding quota is temporarily exhausted.");
        }

        var stopwatch = Stopwatch.StartNew();
        GeminiEmbeddingResult? result = null;
        try
        {
            result = await _client.EmbedContentAsync(
                _options.EmbeddingModel,
                text,
                _options.EmbeddingDimensions,
                cancellationToken);
            var values = result.Values?
                .Select(value => (float)value)
                .ToArray();

            if (values is null || values.Length == 0)
                throw new AiProviderUnavailableException("Gemini returned an empty embedding.");
            if (values.Length != GeminiOptions.RequiredEmbeddingDimensions)
            {
                throw new AiProviderUnavailableException(
                    $"Gemini embedding dimension {values.Length} does not match the required dimension {GeminiOptions.RequiredEmbeddingDimensions}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RecordUsageAsync(context, "success", stopwatch, result, cancellationToken);
            return values;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiProviderUnavailableException)
        {
            await RecordUsageAsync(context, "error", stopwatch, result, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gemini embedding request timed out.");
            await RecordUsageAsync(context, "error", stopwatch, result, CancellationToken.None);
            throw new AiProviderUnavailableException("Gemini embedding request timed out.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini embedding request failed.");
            await RecordUsageAsync(context, "error", stopwatch, result, CancellationToken.None);
            throw new AiProviderUnavailableException("Gemini embedding request failed.", ex);
        }
    }

    private static int? ToTokenCount(double? value)
        => value is >= 0 and <= int.MaxValue
            ? (int)Math.Round(value.Value)
            : null;

    private Task RecordUsageAsync(
        AiRequestContext context,
        string outcome,
        Stopwatch stopwatch,
        GeminiEmbeddingResult? result,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();
        var metadata = result?.BillableCharacterCount is int billableCharacters
            ? new Dictionary<string, object?> { ["billableCharacterCount"] = billableCharacters }
            : null;
        return _telemetry.RecordUsageAsync(
            new AiUsageRecord(
                context.Feature,
                "gemini",
                outcome,
                context.CustomerId,
                context.SessionId,
                _options.EmbeddingModel,
                ToTokenCount(result?.TokenCount),
                TotalTokens: ToTokenCount(result?.TokenCount),
                LatencyMs: (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                Metadata: metadata),
            cancellationToken);
    }
}
