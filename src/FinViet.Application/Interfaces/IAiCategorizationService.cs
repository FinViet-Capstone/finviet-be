using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>Customer-scoped categorization with manual/rule precedence and AI preferences.</summary>
public interface IAiCategorizationService
{
    Task<CategorizationOutcome> CategorizeTransactionAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>Preview a customer-visible category without persisting a financial record.</summary>
    Task<AiClassificationResult> PreviewAsync(
        Guid customerId,
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>Batched variant of <see cref="PreviewAsync"/> for bulk SMS/CSV import: loads the
    /// customer's preference and category catalog once, then classifies every input with bounded
    /// concurrency instead of one request at a time. Results are returned in the same order as
    /// <paramref name="inputs"/>. Each input's failure (AI error or hitting the bulk-import rate
    /// limit) degrades independently to an empty result — one bad/limited row never fails the rest
    /// of the batch, matching how callers already treat a single failed <see cref="PreviewAsync"/>
    /// call.</summary>
    Task<IReadOnlyList<AiClassificationResult>> PreviewManyAsync(
        Guid customerId,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>Background variant of <see cref="CategorizeTransactionAsync"/> for a batch of queued
    /// SePay expenses: applies the manual lock, merchant rules, AI mode and empty-input rules per row,
    /// then classifies the rest with bounded concurrency on the batch rate-limit tier. Every row is
    /// persisted independently (rule, auto-applied, suggestion, unsure or fallback) and a per-row
    /// failure never throws. Rows that are missing, already categorized or locked as manual are
    /// skipped and reported with <c>Applied = false</c>.</summary>
    Task<IReadOnlyList<CategorizationOutcome>> CategorizeManyAsync(
        Guid customerId,
        IReadOnlyList<Guid> transactionIds,
        CancellationToken cancellationToken = default);
}
