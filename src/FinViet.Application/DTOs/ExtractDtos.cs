namespace FinViet.Application.DTOs;

/// <summary>Result of parsing SMS/CSV into candidate transactions with optional AI category
/// suggestions. Nothing is persisted — the client reviews these rows before importing.</summary>
public class ExtractResponse
{
    public List<ExtractedTransactionItem> Rows { get; set; } = new();
    public int TotalScanned { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ExtractedTransactionItem
{
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string? Description { get; set; }
    public DateTime TransactionDate { get; set; }

    /// <summary>Reserved for an AI-suggested category id; not populated by the preview
    /// (the preview only resolves a name). Kept for response-shape compatibility.</summary>
    public string? CategoryId { get; set; }

    /// <summary>AI-suggested category name (null when unresolved).</summary>
    public string? CategoryName { get; set; }

    /// <summary>Model confidence 0.0–1.0 (null when no suggestion was made).</summary>
    public decimal? Confidence { get; set; }
}
