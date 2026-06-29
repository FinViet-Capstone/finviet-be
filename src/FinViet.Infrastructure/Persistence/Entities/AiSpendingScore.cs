using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Closed-period spending-score snapshot. Maps to the v3 <c>ai_spending_scores</c> table:
/// integer <c>score</c>, pg-enum <c>view</c>/<c>color</c>, and Vietnamese text fields. There is
/// no period_end (derived as week_start+6 / month-end) and no persisted weights.
/// </summary>
public partial class AiSpendingScore
{
    /// <summary>Maps to column <c>id</c>.</summary>
    public Guid ScoreId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>pg enum score_view: "weekly" | "monthly".</summary>
    public string View { get; set; } = null!;

    public int Score { get; set; }

    public int? SpikeScore { get; set; }

    public int? BudgetScore { get; set; }

    public int? SavingsScore { get; set; }

    /// <summary>pg enum score_color: "green" | "amber" | "red".</summary>
    public string Color { get; set; } = null!;

    public string? VerdictVi { get; set; }

    public string? ReasonVi { get; set; }

    public string? CommentaryVi { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateTime GeneratedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
