namespace FinViet.Application.DTOs;

/// <summary>A single effective (or scheduled) income/50-30-20 allocation for one calendar month.</summary>
public class IncomeAllocationEntryDto
{
    /// <summary><c>yyyy-MM</c>.</summary>
    public string EffectiveMonth { get; set; } = string.Empty;
    public decimal MonthlyIncome { get; set; }
    public int NeedsPct { get; set; }
    public int WantsPct { get; set; }
    public int SavingsPct { get; set; }
}

/// <summary>
/// What Settings shows: the current month's allocation (locked, read-only — already effective)
/// alongside next month's scheduled draft, if the customer has one pending.
/// </summary>
public class IncomeAllocationSummaryDto
{
    public IncomeAllocationEntryDto Current { get; set; } = null!;
    public IncomeAllocationEntryDto? Pending { get; set; }
}
