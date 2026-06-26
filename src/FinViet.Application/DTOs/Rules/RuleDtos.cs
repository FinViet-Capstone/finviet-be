namespace FinViet.Application.DTOs.Rules;

/// <summary>A merchant-keyword → category rule (api_list #41–43: GET/POST/DELETE /rules).</summary>
public record RuleResponse
{
    public Guid RuleId { get; init; }
    public string MerchantKeyword { get; init; } = null!;
    public string CategoryId { get; init; } = null!;
    public string? CategoryName { get; init; }
    public int AppliedCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateRuleRequest
{
    public string MerchantKeyword { get; init; } = null!;
    public string CategoryId { get; init; } = null!;
}

/// <summary>POST /rules response: the created rule plus how many transactions it retro-applied to.</summary>
public record CreateRuleResponse
{
    public RuleResponse Rule { get; init; } = null!;
    public int AppliedCount { get; init; }
}

/// <summary>A rule resolved for a piece of merchant/description text (used when ingesting new transactions).</summary>
public record RuleMatch(Guid RuleId, string CategoryId, string? CategoryName, string MerchantKeyword);
