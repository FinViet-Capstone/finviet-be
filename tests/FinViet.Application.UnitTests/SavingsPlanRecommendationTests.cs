using FinViet.Application.DTOs;
using FinViet.Infrastructure.Services;

namespace FinViet.Application.UnitTests;

// TC-SAVEPLAN-01..14 — the savings-goal ↔ spending balance decision table and the shared
// per-goal pace formula behind it. Both methods under test are pure, so no database is needed.
public class SavingsPlanRecommendationTests
{
    private const string Month = "2026-08";

    private static IncomeAllocationEntryDto Allocation(
        decimal income, decimal needs = 50m, decimal wants = 30m, decimal savings = 20m)
        => new()
        {
            EffectiveMonth = Month,
            MonthlyIncome = income,
            NeedsPct = needs,
            WantsPct = wants,
            SavingsPct = savings
        };

    /// <summary>
    /// A goal reduced to its monthly figure. Remaining/months/target only matter to the
    /// infeasible branch's "how far to move the deadline / lower the target" numbers, so tests
    /// that don't exercise those can leave them at a consistent 1-month default.
    /// </summary>
    private static IncomeAllocationService.GoalFundingNeed Need(
        decimal monthly, decimal? remaining = null, int months = 1, decimal? target = null)
        => new(monthly, remaining ?? monthly, months, target ?? remaining ?? monthly);

    // ── Statuses that short-circuit before any rebalancing ────────────────────────

    [Fact]
    public void NoActiveGoals_ReportsNoGoals()
    {
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), Array.Empty<IncomeAllocationService.GoalFundingNeed>(), goalsWithoutDeadline: 0);

        Assert.Equal("no_goals", result.Status);
        Assert.Equal(0m, result.Shortfall);
        Assert.Null(result.Proposed);
    }

    [Fact]
    public void GoalsAllWithoutDeadline_ReportsNoGoalsButStillCountsThem()
    {
        // Nothing to divide by without a deadline, so they can't be funded on a schedule — but
        // the client still needs to say "2 goals aren't counted" rather than silently ignore them.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), Array.Empty<IncomeAllocationService.GoalFundingNeed>(), goalsWithoutDeadline: 2);

        Assert.Equal("no_goals", result.Status);
        Assert.Equal(0, result.GoalsConsidered);
        Assert.Equal(2, result.GoalsWithoutDeadline);
    }

    [Fact]
    public void ZeroIncome_ReportsNoIncomeAndFullShortfall()
    {
        // Every cap is a percentage of income, so no split funds anything — moving percentages
        // around would be a lie. The whole requirement is the shortfall.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(0m), new[] { Need(1_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("no_income", result.Status);
        Assert.Equal(1_000_000m, result.Shortfall);
        Assert.Null(result.Proposed);
    }

    [Fact]
    public void AllocationNotSummingTo100_ReportsInvalidAllocation()
    {
        // 50/30/25 = 105. A proposal derived from this would be rejected by the schedule
        // validator, so it must never be emitted in the first place.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(20_000_000m, needs: 50m, wants: 30m, savings: 25m),
            new[] { Need(6_000_000m) },
            goalsWithoutDeadline: 0);

        Assert.Equal("invalid_allocation", result.Status);
        Assert.Null(result.Proposed);
    }

    // ── On track ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GoalsFitWithinSavingsCap_ReportsOnTrack()
    {
        // 20% of 20M = 4M cap, goals need 3M.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(2_000_000m), Need(1_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("on_track", result.Status);
        Assert.Equal(3_000_000m, result.RequiredMonthlySavings);
        Assert.Equal(4_000_000m, result.CurrentSavingsCap);
        Assert.Equal(0m, result.Shortfall);
        Assert.Null(result.Proposed);
    }

    [Fact]
    public void GoalsExactlyMatchSavingsCap_ReportsOnTrack()
    {
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(4_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("on_track", result.Status);
        Assert.Equal(0m, result.Shortfall);
    }

    // ── Adjustable: the actual auto-balance ───────────────────────────────────────

    [Fact]
    public void GoalsOutrunCap_ProposesSplitThatFundsThem()
    {
        // Cap 4M, goals need 5M → savings must become 25% of 20M.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(5_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("adjustable", result.Status);
        Assert.Equal(1_000_000m, result.Shortfall);
        Assert.NotNull(result.Proposed);
        Assert.Equal(25m, result.Proposed!.SavingsPct);
        Assert.Equal(5_000_000m, result.ProposedSavingsCap);
    }

    [Fact]
    public void Rebalance_TakesOnlyFromWantsAndNeverTouchesNeeds()
    {
        // The safety rule this feature is built on: essentials are never auto-cut.
        var current = Allocation(20_000_000m);
        var result = IncomeAllocationService.BuildRecommendation(
            Month, current, new[] { Need(5_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal(current.NeedsPct, result.Proposed!.NeedsPct);
        Assert.Equal(10_000_000m, result.ProposedNeedsCap);
        Assert.Equal(25m, result.Proposed.WantsPct);
        Assert.Equal(5_000_000m, result.ProposedWantsCap);
    }

    [Fact]
    public void ProposedSplitAlwaysSumsToExactly100_EvenWhenThePctDoesNotDivideEvenly()
    {
        // 700k of 3M is 23.333…%, which rounds. Wants is derived by subtraction precisely so the
        // three still total 100 — anything else would be rejected on apply.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(3_000_000m), new[] { Need(700_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("adjustable", result.Status);
        var proposed = result.Proposed!;
        Assert.Equal(23.33m, proposed.SavingsPct);
        Assert.Equal(100m, proposed.NeedsPct + proposed.WantsPct + proposed.SavingsPct);
    }

    [Fact]
    public void RebalanceDownToTheWantsFloorExactly_IsStillAdjustable()
    {
        // Savings 20% + all of Wants above the 5% floor = 45% ceiling → 9M of 20M.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(9_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("adjustable", result.Status);
        Assert.Equal(45m, result.Proposed!.SavingsPct);
        Assert.Equal(IncomeAllocationService.WantsFloorPct, result.Proposed.WantsPct);
    }

    [Fact]
    public void ProposalIsScheduledForNextMonth_NotTheMonthQueried()
    {
        // Allocation changes only ever take effect next month; a proposal for the current month
        // would be unappliable.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(5_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal(
            IncomeAllocationService.MonthKey(DateTime.UtcNow.AddMonths(1)),
            result.Proposed!.EffectiveMonth);
    }

    // ── Infeasible ────────────────────────────────────────────────────────────────

    [Fact]
    public void GoalsBeyondTheWantsFloor_ReportInfeasibleWithTheCeiling()
    {
        // 10M needed but only 9M reachable without cutting Needs.
        var result = IncomeAllocationService.BuildRecommendation(
            Month, Allocation(20_000_000m), new[] { Need(10_000_000m) }, goalsWithoutDeadline: 0);

        Assert.Equal("infeasible", result.Status);
        Assert.Null(result.Proposed);
        Assert.Equal(9_000_000m, result.MaxFundableMonthlySavings);
        Assert.Equal(6_000_000m, result.Shortfall);
    }

    [Fact]
    public void Infeasible_SaysHowManyMonthsWouldBeNeeded()
    {
        // The live case that motivated this: income 5M split 50/30/20, a 32M goal with 5M saved
        // and 2 months left. 27M remaining ÷ 2 = 13.5M/month against a 2.25M ceiling.
        // 27M ÷ 2.25M = exactly 12 months.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(5_000_000m),
            new[] { Need(13_500_000m, remaining: 27_000_000m, months: 2, target: 32_000_000m) },
            goalsWithoutDeadline: 0);

        Assert.Equal("infeasible", result.Status);
        Assert.Equal(2_250_000m, result.MaxFundableMonthlySavings);
        Assert.Equal(27_000_000m, result.TotalRemainingAmount);
        Assert.Equal(12, result.MinimumMonthsToFund);
    }

    [Fact]
    public void Infeasible_SaysHowFarTheSingleGoalWouldHaveToDrop()
    {
        // Keeping the 2-month deadline, the plan can fund 2.25M × 2 = 4.5M more, on top of the
        // 5M already saved → a 9.5M target is the most that fits.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(5_000_000m),
            new[] { Need(13_500_000m, remaining: 27_000_000m, months: 2, target: 32_000_000m) },
            goalsWithoutDeadline: 0);

        Assert.Equal(9_500_000m, result.MaximumFundableTargetAmount);
    }

    [Fact]
    public void Infeasible_WithSeveralGoals_OffersMonthsButNoSingleTargetToLower()
    {
        // There is no one target to lower across several goals, and splitting the ceiling
        // between them would be an invented answer — so that field stays null on purpose.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(5_000_000m),
            new[]
            {
                Need(8_000_000m, remaining: 16_000_000m, months: 2, target: 16_000_000m),
                Need(5_500_000m, remaining: 11_000_000m, months: 2, target: 11_000_000m)
            },
            goalsWithoutDeadline: 0);

        Assert.Equal("infeasible", result.Status);
        Assert.Equal(27_000_000m, result.TotalRemainingAmount);
        Assert.Equal(12, result.MinimumMonthsToFund);
        Assert.Null(result.MaximumFundableTargetAmount);
    }

    [Fact]
    public void Infeasible_RoundsTheMonthsUp_NeverDownIntoAnUnfundablePlan()
    {
        // 10M ÷ 2.25M = 4.44 → 5 months. Rounding down to 4 would hand back a deadline that
        // still doesn't work, which is worse than no suggestion at all.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(5_000_000m),
            new[] { Need(10_000_000m, remaining: 10_000_000m, months: 1, target: 10_000_000m) },
            goalsWithoutDeadline: 0);

        Assert.Equal(5, result.MinimumMonthsToFund);
    }

    [Fact]
    public void Infeasible_WithNoHeadroomAtAll_OffersNoDeadlineSuggestion()
    {
        // Savings 0% and Wants already at the floor → ceiling is 0, so no number of months ever
        // funds anything. Dividing by it would produce infinity, not advice.
        var result = IncomeAllocationService.BuildRecommendation(
            Month,
            Allocation(5_000_000m, needs: 95m, wants: 5m, savings: 0m),
            new[] { Need(1_000_000m, remaining: 2_000_000m, months: 2, target: 2_000_000m) },
            goalsWithoutDeadline: 0);

        Assert.Equal("infeasible", result.Status);
        Assert.Equal(0m, result.MaxFundableMonthlySavings);
        Assert.Null(result.MinimumMonthsToFund);
        Assert.Null(result.MaximumFundableTargetAmount);
    }

    // ── The shared per-goal pace formula ──────────────────────────────────────────

    [Fact]
    public void ComputeMonthlyPace_DividesRemainingOverWholeMonthsLeft()
    {
        var (months, monthly) = SavingGoalService.ComputeMonthlyPace(
            6_000_000m, new DateOnly(2026, 11, 28), new DateOnly(2026, 8, 28));

        Assert.Equal(3, months);
        Assert.Equal(2_000_000m, monthly);
    }

    [Fact]
    public void ComputeMonthlyPace_DeadlineDayBeforeTodaysDay_DropsAPartialMonth()
    {
        // 27 Nov is short of a full third month from 28 Aug, so it's 2 whole months, not 3.
        var (months, monthly) = SavingGoalService.ComputeMonthlyPace(
            6_000_000m, new DateOnly(2026, 11, 27), new DateOnly(2026, 8, 28));

        Assert.Equal(2, months);
        Assert.Equal(3_000_000m, monthly);
    }

    [Fact]
    public void ComputeMonthlyPace_DeadlineInsideThisMonth_NeedsTheWholeRemainderAtOnce()
    {
        // Guards the division: 0 whole months left must not divide by zero.
        var (months, monthly) = SavingGoalService.ComputeMonthlyPace(
            6_000_000m, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 28));

        Assert.Equal(0, months);
        Assert.Equal(6_000_000m, monthly);
    }

    [Fact]
    public void ComputeMonthlyPace_NothingLeftToSave_NeedsNothing()
    {
        var (_, monthly) = SavingGoalService.ComputeMonthlyPace(
            0m, new DateOnly(2026, 11, 28), new DateOnly(2026, 8, 28));

        Assert.Equal(0m, monthly);
    }
}
