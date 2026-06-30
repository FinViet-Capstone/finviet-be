using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IBeneficiaryRuleService
{
    /// <summary>Override a transaction's category. Writes a category_correction_log row.</summary>
    Task<CategorizationOutcome> OverrideCategoryAsync(
        Guid customerId, Guid transactionId, OverrideCategoryRequest request,
        CancellationToken cancellationToken = default);
}
