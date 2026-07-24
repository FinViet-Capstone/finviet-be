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

    /// <summary>True when the AI provider was unavailable and the transaction was queued for re-processing.</summary>
    public bool Queued { get; set; }

    /// <summary>Source of the decision: RULE, AI, or FALLBACK.</summary>
    public string Source { get; set; } = "FALLBACK";
}
