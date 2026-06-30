using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>Thin wrapper over the Google Gemini REST API. All methods throw
/// <see cref="Exceptions.GeminiUnavailableException"/> on transport/parse failure so callers
/// can apply their own fallback.</summary>
public interface IGeminiClient
{
    /// <summary>Classify a free-text beneficiary/note into one of <paramref name="allowedCategories"/>.
    /// Handles Vietnamese abbreviations/slang in the prompt. Returns the chosen category name
    /// (must be one of the supplied names, else null) plus a confidence.</summary>
    Task<AiClassificationResult> ClassifyAsync(
        string input,
        IReadOnlyList<string> allowedCategories,
        CancellationToken cancellationToken = default);

    /// <summary>Generate a short (1–2 sentence) Vietnamese comment for a spending score.</summary>
    Task<string> GenerateScoreCommentAsync(
        string scoreContext,
        CancellationToken cancellationToken = default);

    /// <summary>Generate a 150–200 word Vietnamese weekly financial narrative from a summary.</summary>
    Task<string> GenerateReportAsync(
        string reportContext,
        CancellationToken cancellationToken = default);

    /// <summary>Answer a Vietnamese question given an aggregated financial-context block and
    /// recent conversation turns.</summary>
    Task<string> ChatAsync(
        string contextBlock,
        IReadOnlyList<AiChatTurn> recentTurns,
        string question,
        CancellationToken cancellationToken = default);
}
