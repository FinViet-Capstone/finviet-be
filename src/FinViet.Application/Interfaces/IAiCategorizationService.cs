using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>Orchestrates transaction categorization: beneficiary rule → Gemini → fallback.</summary>
public interface IAiCategorizationService
{
    /// <summary>Classify and persist a category for a single transaction. Checks beneficiary_rule
    /// first; otherwise calls Gemini. On Gemini failure, sets the transaction to "Chưa phân loại"
    /// and enqueues it for re-processing.</summary>
    Task<CategorizationOutcome> CategorizeTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>Preview classification for free text without persisting (UI suggestion badge).</summary>
    Task<AiClassificationResult> PreviewAsync(
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>Re-process a queued item by its transaction id. Used by the background processor.
    /// Returns true if the transaction was successfully classified (queue row can be marked DONE),
    /// false if Gemini is still unavailable (caller should retry later).</summary>
    Task<bool> ReprocessAsync(
        Guid transactionId,
        string rawInput,
        CancellationToken cancellationToken = default);
}
