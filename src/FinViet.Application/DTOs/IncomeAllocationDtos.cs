namespace FinViet.Application.DTOs;

/// <summary>A single effective (or scheduled) income/50-30-20 allocation for one calendar month.</summary>
public class IncomeAllocationEntryDto
{
    /// <summary><c>yyyy-MM</c>.</summary>
    public string EffectiveMonth { get; set; } = string.Empty;
    public decimal MonthlyIncome { get; set; }
    public decimal NeedsPct { get; set; }
    public decimal WantsPct { get; set; }
    public decimal SavingsPct { get; set; }
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

/// <summary>
/// Whether the customer's active saving goals are fundable out of their own Savings bucket, and —
/// when they aren't — the rebalanced 3-bucket split that would fund them plus the spending caps
/// that split implies. Read-only: nothing here is persisted until the customer applies it.
/// </summary>
public class SavingsPlanRecommendationDto
{
    /// <summary><c>yyyy-MM</c> this was computed for.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>
    /// One of <c>on_track</c> (goals already fit the Savings cap), <c>adjustable</c> (they don't,
    /// but moving Wants into Savings covers it — <see cref="Proposed"/> is set),
    /// <c>infeasible</c> (even at the Wants floor the plan can't fund them —
    /// <see cref="MaxFundableMonthlySavings"/> is set), <c>no_goals</c> (nothing to fund),
    /// <c>no_income</c> (monthly income is 0, so percentages of it are meaningless), or
    /// <c>invalid_allocation</c> (the effective split doesn't sum to 100, so any proposal built
    /// from it would be rejected on apply). The client maps these to localized copy.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public decimal MonthlyIncome { get; set; }

    /// <summary>Sum of the per-goal monthly figure across active goals that still have a deadline.</summary>
    public decimal RequiredMonthlySavings { get; set; }

    /// <summary>What the current split actually allocates to Savings, in đồng.</summary>
    public decimal CurrentSavingsCap { get; set; }

    /// <summary>How much the goals outrun the current Savings cap. 0 when on track.</summary>
    public decimal Shortfall { get; set; }

    /// <summary>Active, unmet goals with a deadline — the ones counted in the figure above.</summary>
    public int GoalsConsidered { get; set; }

    /// <summary>
    /// Active, unmet goals with no deadline. Deliberately excluded from the total (with no
    /// deadline there's no monthly figure to derive), surfaced so the client can say so.
    /// </summary>
    public int GoalsWithoutDeadline { get; set; }

    /// <summary>The split the numbers above were measured against.</summary>
    public IncomeAllocationEntryDto Current { get; set; } = null!;

    /// <summary>The split that would fund the goals. Only set when <c>Status</c> is <c>adjustable</c>.</summary>
    public IncomeAllocationEntryDto? Proposed { get; set; }

    /// <summary>Spending caps in đồng implied by <see cref="Proposed"/>. Null unless it is set.</summary>
    public decimal? ProposedNeedsCap { get; set; }
    public decimal? ProposedWantsCap { get; set; }
    public decimal? ProposedSavingsCap { get; set; }

    /// <summary>
    /// The most the plan could ever put into Savings without cutting Needs. Only set when
    /// <c>Status</c> is <c>infeasible</c>, so the client can show how far short the goals are.
    /// </summary>
    public decimal? MaxFundableMonthlySavings { get; set; }
}
