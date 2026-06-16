namespace FinViet.Application.DTOs.Ai;

/// <summary>Result of an AI categorization call. Null result is signaled by the caller treating
/// a thrown <see cref="GeminiUnavailableException"/> as "service down".</summary>
public class AiClassificationResult
{
    /// <summary>Exact category name chosen from the supplied closed set, or null if unresolved.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Model confidence 0.0–1.0.</summary>
    public decimal Confidence { get; set; }
}
