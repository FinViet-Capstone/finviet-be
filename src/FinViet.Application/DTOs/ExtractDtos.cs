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

    /// <summary>OCR field confidence (0.0–1.0). Null for deterministic SMS/CSV parsers or older
    /// providers that do not expose field-level confidence.</summary>
    public decimal? AmountConfidence { get; set; }
    public decimal? MerchantConfidence { get; set; }
    public decimal? TransactionDateConfidence { get; set; }

    /// <summary>AI-suggested (or rule-matched) category id, resolved from the model's chosen
    /// category name against the customer's category set. Null when unresolved.</summary>
    public string? CategoryId { get; set; }

    /// <summary>AI-suggested category name (null when unresolved).</summary>
    public string? CategoryName { get; set; }

    /// <summary>Model confidence 0.0–1.0 (null when no suggestion was made).</summary>
    public decimal? Confidence { get; set; }

    /// <summary>Original source-file row, column-by-column, exactly as the bank exported it —
    /// not just the columns mapped to normalized fields above. CSV/XLSX import only (null for
    /// SMS/photo rows, and for CSV rows parsed via the header-less legacy layout, which has no
    /// header text to pair values with). Optional/additive: clients render their own raw-data
    /// view only when present.</summary>
    public List<RawFieldPair>? RawFields { get; set; }
}

/// <summary>One original column value from a source CSV/Excel row, preserved as-is.</summary>
public class RawFieldPair
{
    public string Header { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
