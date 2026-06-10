namespace FinViet.Application.DTOs.SavingGoals;

public class SavingGoalResponse
{
    public Guid GoalId { get; set; }
    public Guid CustomerId { get; set; }
    public string GoalName { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public DateOnly? Deadline { get; set; }

    // ── Progress tracking (function 44) ──────────────────────
    public decimal RemainingAmount { get; set; }
    public decimal ProgressPercent { get; set; }
    public int? DaysRemaining { get; set; }
    public bool IsCompleted { get; set; }

    // ── Auto-calculated monthly saving (function 43) ─────────
    /// <summary>Amount to set aside each month to reach the target by the deadline. Null if no deadline.</summary>
    public decimal? MonthlySavingNeeded { get; set; }

    /// <summary>Whole months left until the deadline (used for monthly calc). Null if no deadline.</summary>
    public int? MonthsRemaining { get; set; }
}
