using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IBeneficiaryRuleService
{
    Task<IReadOnlyList<BeneficiaryRuleResponse>> GetRulesAsync(
        Guid customerId, CancellationToken cancellationToken = default);

    Task<BeneficiaryRuleResponse> UpsertRuleAsync(
        Guid customerId, UpsertBeneficiaryRuleRequest request, CancellationToken cancellationToken = default);

    Task DeleteRuleAsync(Guid customerId, Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Override a transaction's category. Writes a category_correction_log row. If
    /// <see cref="OverrideCategoryRequest.CreateRule"/> is set, creates/updates a beneficiary rule
    /// and applies it retroactively to all of the customer's matching transactions.</summary>
    Task<CategorizationOutcome> OverrideCategoryAsync(
        Guid customerId, Guid transactionId, OverrideCategoryRequest request,
        CancellationToken cancellationToken = default);
}
