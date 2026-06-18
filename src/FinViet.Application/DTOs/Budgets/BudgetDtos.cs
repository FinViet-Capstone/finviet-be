namespace FinViet.Application.DTOs.Budgets;

public class BudgetResponse
{
    public Guid Id { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public Guid? WalletId { get; set; }
    public decimal MonthlyLimit { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining { get; set; }
    public decimal Percentage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
}

public class UpsertBudgetRequest
{
    public string CategoryId { get; set; } = string.Empty;
    public Guid? WalletId { get; set; }
    public decimal MonthlyLimit { get; set; }
}

public class UpdateBudgetRequest
{
    public decimal MonthlyLimit { get; set; }
}

public class BucketSummaryListResponse
{
    public string Month { get; set; } = string.Empty;
    public decimal MonthlyIncome { get; set; }
    public decimal BudgetAdherenceScore { get; set; }
    public decimal UncategorizedRatio { get; set; }
    public bool UncategorizedWarning { get; set; }
    public IReadOnlyList<BucketSummaryResponse> Buckets { get; set; } = Array.Empty<BucketSummaryResponse>();
}

public class BucketSummaryResponse
{
    public string Bucket { get; set; } = string.Empty;
    public decimal AllocationPct { get; set; }
    public decimal AllocationCap { get; set; }
    public decimal CategoryLimitTotal { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining { get; set; }
    public decimal Percentage { get; set; }
    public bool OverAllocated { get; set; }
    public decimal ExpectedSpent { get; set; }
    public decimal PaceDeviation { get; set; }
    public string PaceStatus { get; set; } = string.Empty;
}
