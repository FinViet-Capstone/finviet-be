namespace FinViet.Application.DTOs.Ai;

public class SpendingScoreResult
{
    /// <summary>"WEEKLY" or "MONTHLY".</summary>
    public string PeriodType { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>0–100.</summary>
    public decimal FinalScore { get; set; }

    public decimal? SpikeScore { get; set; }

    public decimal? BudgetScore { get; set; }

    public decimal? SavingsScore { get; set; }

    /// <summary>Effective weights actually applied after re-normalization, keyed by metric.</summary>
    public Dictionary<string, decimal> Weights { get; set; } = new();

    /// <summary>"GREEN" | "YELLOW" | "RED".</summary>
    public string ColorBadge { get; set; } = "YELLOW";

    /// <summary>
    /// False when the customer has no expense transactions inside [PeriodStart, PeriodEnd] —
    /// the score fields then carry the neutral/metric fallbacks and the FE should render a
    /// "no data yet" state instead of presenting them as a real assessment.
    /// </summary>
    public bool HasData { get; set; } = true;

    /// <summary>Short Vietnamese comment (may be empty if AI comment was skipped/unavailable).</summary>
    public string? Comment { get; set; }
}
