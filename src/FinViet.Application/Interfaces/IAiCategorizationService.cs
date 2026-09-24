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

    Task<bool> ReprocessAsync(
        Guid customerId,
        Guid transactionId,
        string rawInput,
        CancellationToken cancellationToken = default);
}
