using FinViet.Application.DTOs.Rules;

namespace FinViet.Application.Interfaces;

/// <summary>Manages customer merchant-keyword auto-categorization rules.</summary>
public interface IMerchantRuleService
{
    Task<IReadOnlyList<RuleResponse>> GetRulesAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Create a rule and retro-apply it to matching transactions. Returns the rule + applied count.</summary>
    Task<CreateRuleResponse> CreateRuleAsync(Guid customerId, CreateRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Delete a rule. Returns false when not found; throws when owned by another customer.</summary>
    Task<bool> DeleteRuleAsync(Guid customerId, Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the best (longest-match) rule for the given merchant/description text, or null when
    /// none matches. Read-only — used when ingesting a new transaction (create / SMS / CSV) so a
    /// rule takes precedence over AI categorization (BUSINESS_LOGIC §2b).
    /// </summary>
    Task<RuleMatch?> ResolveAsync(Guid customerId, string? merchant, string? description, CancellationToken cancellationToken = default);

    /// <summary>Increment a rule's applied_count after it has been applied to a newly-saved transaction.</summary>
    Task IncrementAppliedAsync(Guid ruleId, int by = 1, CancellationToken cancellationToken = default);
}
