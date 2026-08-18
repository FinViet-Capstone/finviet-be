namespace FinViet.Application.DTOs.Ai;

/// <summary>Result of an AI categorization call. Null result is signaled by the caller treating
/// a thrown <see cref="AiProviderUnavailableException"/> as "service down".</summary>
public class AiClassificationResult
{
    /// <summary>Exact category name chosen from the supplied closed set, or null if unresolved.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Model confidence 0.0–1.0.</summary>
    public decimal Confidence { get; set; }

    /// <summary>Category id resolved from <see cref="CategoryName"/> against the customer's category
    /// set. Null when the provider returned no name, or the name doesn't resolve to a real category.
    /// Set by the categorization-service layer (which owns the name→id lookup), not by the raw
    /// provider client.</summary>
    public string? CategoryId { get; set; }
}
