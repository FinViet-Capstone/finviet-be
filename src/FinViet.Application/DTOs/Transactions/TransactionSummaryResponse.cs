namespace FinViet.Application.DTOs.Transactions;

/// <summary>Monthly summary for the calendar/report views. Spec §4 GET /transactions/summary.
/// Excludes transfers from all aggregates.</summary>
public class TransactionSummaryResponse
{
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
    public List<CategorySummaryItem> ByCategory { get; set; } = new();
    public List<DaySummaryItem> ByDay { get; set; } = new();
    public List<MerchantSummaryItem> TopMerchants { get; set; } = new();
}

public class CategorySummaryItem
{
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal Total { get; set; }
}

public class DaySummaryItem
{
    public DateOnly Date { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
}

public class MerchantSummaryItem
{
    public string Merchant { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>One extracted candidate row from SMS/CSV (not yet persisted). Spec §4 POST /extract/*.</summary>
public class ExtractedTransactionItem
{
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public string? Description { get; set; }
    public DateTime TransactionDate { get; set; }

    /// <summary>AI-suggested category slug (may be null when unresolved).</summary>
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal? Confidence { get; set; }
}

public class ExtractResponse
{
    public List<ExtractedTransactionItem> Rows { get; set; } = new();
    public int TotalScanned { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}
