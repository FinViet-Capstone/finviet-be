using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>Provider-neutral client for FinViet's text-generation features. All methods throw
/// <see cref="Exceptions.AiProviderUnavailableException"/> on provider transport/parse failure so
/// callers can apply their existing fallback.</summary>
public interface IAiModelClient
{
    Task<AiClassificationResult> ClassifyAsync(
        string input,
        IReadOnlyList<string> allowedCategories,
        CancellationToken cancellationToken = default);

    Task<string> GenerateScoreCommentAsync(
        string scoreContext,
        CancellationToken cancellationToken = default);

    Task<string> GenerateReportAsync(
        string reportContext,
        CancellationToken cancellationToken = default);

    Task<string> ChatAsync(
        string contextBlock,
        IReadOnlyList<AiChatTurn> recentTurns,
        string question,
        CancellationToken cancellationToken = default);
}
