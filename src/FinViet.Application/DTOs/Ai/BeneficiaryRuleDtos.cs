namespace FinViet.Application.DTOs.Ai;

public class BeneficiaryRuleResponse
{
    public Guid RuleId { get; set; }
    public string MatchText { get; set; } = null!;
    public string CategoryId { get; set; } = null!;
    public string? CategoryName { get; set; }
    public bool IsRecurring { get; set; }
}

public class UpsertBeneficiaryRuleRequest
{
    public string MatchText { get; set; } = null!;
    public string CategoryId { get; set; } = null!;
    public bool IsRecurring { get; set; }
}

/// <summary>Override a transaction's category, optionally creating a retroactive beneficiary rule.</summary>
public class OverrideCategoryRequest
{
    public string CategoryId { get; set; } = null!;

    /// <summary>If true, create/update a beneficiary rule from this transaction's beneficiary and
    /// apply it retroactively to all matching transactions.</summary>
    public bool CreateRule { get; set; }

    /// <summary>Mark the created rule as recurring (excluded from spike detection as a fixed bill).</summary>
    public bool IsRecurring { get; set; }
}
