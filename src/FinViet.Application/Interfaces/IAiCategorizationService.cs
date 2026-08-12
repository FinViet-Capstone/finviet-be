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

    Task<bool> ReprocessAsync(
        Guid customerId,
        Guid transactionId,
        string rawInput,
        CancellationToken cancellationToken = default);
}
