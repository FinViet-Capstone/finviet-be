namespace FinViet.Application.DTOs.Ai;

/// <summary>Outcome of categorizing a transaction.</summary>
public class CategorizationOutcome
{
    public Guid TransactionId { get; set; }

    /// <summary>Resolved category id written to the transaction.</summary>
    public string? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public decimal? Confidence { get; set; }

    public bool IsAiClassified { get; set; }

    /// <summary>Retained for backward compatibility. No retry queue is currently exposed.</summary>
    public bool Queued { get; set; }

    public bool Applied { get; set; }

    public string? SuggestedCategoryId { get; set; }

    public string? SuggestedCategoryName { get; set; }

    public string? Reason { get; set; }

    /// <summary>Source: MANUAL, RULE, AI_AUTO, AI_SUGGESTION, OFF, or FALLBACK.</summary>
    public string Source { get; set; } = "FALLBACK";
}
