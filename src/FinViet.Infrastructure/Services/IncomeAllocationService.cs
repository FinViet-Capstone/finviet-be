using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

public class IncomeAllocationService : IIncomeAllocationService
{
    private readonly FinVietDbContext _db;

    public IncomeAllocationService(FinVietDbContext db) => _db = db;

    public async Task<IncomeAllocationEntryDto> GetEffectiveAsync(
        Guid customerId, string month, CancellationToken cancellationToken = default)
    {
        // Per-customer row count is small (one per calendar month since signup at most), so
        // resolving "latest row <= month" in memory avoids relying on string-relational
        // operator translation for the EffectiveMonth column.
        var rows = await _db.IncomeAllocationSettings
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var effective = ResolveEffectiveRow(rows, month);

        if (effective is not null)
            return ToDto(effective);

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");

        return new IncomeAllocationEntryDto
        {
            EffectiveMonth = month,
            MonthlyIncome = customer.MonthlyIncomeExpected ?? 0m,
            NeedsPct = customer.NeedsPct,
            WantsPct = customer.WantsPct,
            SavingsPct = customer.SavingsPct
        };
    }

    public async Task<IncomeAllocationSummaryDto> GetSummaryAsync(
        Guid customerId, string? month = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(month))
        {
            var requestedMonth = NormalizeMonth(month);
            var requested = await GetEffectiveAsync(customerId, requestedMonth, cancellationToken);
            return new IncomeAllocationSummaryDto { Current = requested, Pending = null };
        }

        var currentMonth = MonthKey(DateTime.UtcNow);
        var nextMonth = MonthKey(DateTime.UtcNow.AddMonths(1));

        var current = await GetEffectiveAsync(customerId, currentMonth, cancellationToken);

        var pendingRow = await _db.IncomeAllocationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CustomerId == customerId && x.EffectiveMonth == nextMonth,
                cancellationToken);

        return new IncomeAllocationSummaryDto
        {
            Current = current,
            Pending = pendingRow is null ? null : ToDto(pendingRow)
        };
    }

    public async Task<IncomeAllocationEntryDto> ScheduleNextMonthAsync(
        Guid customerId,
        decimal monthlyIncome,
        decimal needsPct,
        decimal wantsPct,
        decimal savingsPct,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!exists)
            throw new NotFoundException("Customer not found.");

        var nextMonth = MonthKey(DateTime.UtcNow.AddMonths(1));

        var entry = await _db.IncomeAllocationSettings.FirstOrDefaultAsync(
            x => x.CustomerId == customerId && x.EffectiveMonth == nextMonth,
            cancellationToken);

        if (entry is null)
        {
            entry = new IncomeAllocationSetting
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                EffectiveMonth = nextMonth,
                CreatedAt = DateTime.UtcNow
            };
            _db.IncomeAllocationSettings.Add(entry);
        }

        entry.MonthlyIncome = monthlyIncome;
        entry.NeedsPct = needsPct;
        entry.WantsPct = wantsPct;
        entry.SavingsPct = savingsPct;

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(entry);
    }

    public async Task<SavingsPlanRecommendationDto> GetSavingsPlanRecommendationAsync(
        Guid customerId, string? month = null, CancellationToken cancellationToken = default)
    {
        var monthKey = string.IsNullOrWhiteSpace(month)
            ? MonthKey(DateTime.UtcNow)
            : NormalizeMonth(month);

        // Throws NotFound for an unknown customer, so no separate existence check is needed.
        var current = await GetEffectiveAsync(customerId, monthKey, cancellationToken);

        var goals = await _db.SavingGoals
            .AsNoTracking()
            .Where(g => g.CustomerId == customerId && !g.IsDeleted && !g.IsCompleted)
            .Select(g => new { g.TargetAmount, g.CurrentAmount, g.Deadline })
            .ToListAsync(cancellationToken);

        // DateTime.UtcNow (not ICT) to match SavingGoalService.ToResponse exactly — shifting the
        // reference day here would make this aggregate disagree with the per-goal numbers the
        // goal list already shows, which is the specific incoherence this feature exists to end.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var requiredPerGoal = new List<decimal>();
        var goalsWithoutDeadline = 0;

        foreach (var goal in goals)
        {
            var remaining = Math.Max(0m, goal.TargetAmount - (goal.CurrentAmount ?? 0m));

            // Fully funded but not yet flagged completed (the flag is only written on the
            // contribute/withdraw paths) — nothing left to fund, so it shouldn't inflate the total.
            if (remaining <= 0m)
                continue;

            if (!goal.Deadline.HasValue)
            {
                goalsWithoutDeadline++;
                continue;
            }

            requiredPerGoal.Add(
                SavingGoalService.ComputeMonthlyPace(remaining, goal.Deadline.Value, today)
                    .MonthlySavingNeeded);
        }

        return BuildRecommendation(monthKey, current, requiredPerGoal, goalsWithoutDeadline);
    }

    public async Task<IncomeAllocationEntryDto> ApplySavingsPlanRecommendationAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        // Recomputed here rather than accepted from the caller: a proposal the client is holding
        // may have been invalidated by a goal edit or contribution since it was fetched, and
        // applying a stale split would quietly misallocate real money.
        var recommendation = await GetSavingsPlanRecommendationAsync(customerId, null, cancellationToken);

        if (recommendation.Status != RecommendationStatus.Adjustable || recommendation.Proposed is null)
        {
            throw new BusinessRuleException(
                "There is no savings-plan adjustment to apply right now.",
                recommendation.Status);
        }

        return await ScheduleNextMonthAsync(
            customerId,
            recommendation.Proposed.MonthlyIncome,
            recommendation.Proposed.NeedsPct,
            recommendation.Proposed.WantsPct,
            recommendation.Proposed.SavingsPct,
            cancellationToken);
    }

    /// <summary>Status values for <see cref="SavingsPlanRecommendationDto.Status"/>.</summary>
    internal static class RecommendationStatus
    {
        public const string OnTrack = "on_track";
        public const string Adjustable = "adjustable";
        public const string Infeasible = "infeasible";
        public const string NoGoals = "no_goals";
        public const string NoIncome = "no_income";
        public const string InvalidAllocation = "invalid_allocation";
    }

    /// <summary>
    /// The smallest share of income the Wants bucket is allowed to be squeezed to. Rebalancing
    /// takes only from Wants: automatically advising someone to cut essentials (Needs) to chase a
    /// savings target is bad guidance, so when Wants alone can't cover the gap the answer is
    /// "infeasible" plus the numbers, not a smaller Needs bucket.
    /// </summary>
    internal const decimal WantsFloorPct = 5m;

    /// <summary>
    /// Pure/no I/O so the whole decision table is unit-testable without a database.
    /// <paramref name="requiredPerGoal"/> holds the per-goal monthly figure for active, unmet,
    /// deadlined goals only.
    /// </summary>
    internal static SavingsPlanRecommendationDto BuildRecommendation(
        string month,
        IncomeAllocationEntryDto current,
        IReadOnlyList<decimal> requiredPerGoal,
        int goalsWithoutDeadline)
    {
        var income = current.MonthlyIncome;
        var required = Math.Round(requiredPerGoal.Sum(), 2);
        var savingsCap = Math.Round(income * current.SavingsPct / 100m, 2);

        var result = new SavingsPlanRecommendationDto
        {
            Month = month,
            MonthlyIncome = income,
            RequiredMonthlySavings = required,
            CurrentSavingsCap = savingsCap,
            GoalsConsidered = requiredPerGoal.Count,
            GoalsWithoutDeadline = goalsWithoutDeadline,
            Current = current,
            Shortfall = 0m
        };

        if (requiredPerGoal.Count == 0)
        {
            result.Status = RecommendationStatus.NoGoals;
            return result;
        }

        if (income <= 0m)
        {
            // Every bucket cap is a percentage of income, so with no income on record there is no
            // split that funds anything — the fix is to set an income, not to move percentages.
            result.Status = RecommendationStatus.NoIncome;
            result.Shortfall = required;
            return result;
        }

        var shortfall = Math.Max(0m, required - savingsCap);
        result.Shortfall = shortfall;

        if (shortfall <= 0m)
        {
            result.Status = RecommendationStatus.OnTrack;
            return result;
        }

        // Guard rather than silently normalize: a split that doesn't total 100 would produce a
        // proposal ScheduleNextMonthAsync's validator rejects, so surface it instead of emitting
        // an unappliable recommendation.
        if (current.NeedsPct + current.WantsPct + current.SavingsPct != 100m)
        {
            result.Status = RecommendationStatus.InvalidAllocation;
            return result;
        }

        var neededSavingsPct = Math.Round(required / income * 100m, 2);
        var maxSavingsPct = current.SavingsPct + Math.Max(0m, current.WantsPct - WantsFloorPct);

        if (neededSavingsPct > maxSavingsPct)
        {
            result.Status = RecommendationStatus.Infeasible;
            result.MaxFundableMonthlySavings = Math.Round(income * maxSavingsPct / 100m, 2);
            return result;
        }

        // Needs is carried over untouched and Wants absorbs the whole move, derived by
        // subtraction so the three always total exactly 100 despite the rounding above.
        var proposedNeedsPct = current.NeedsPct;
        var proposedSavingsPct = neededSavingsPct;
        var proposedWantsPct = 100m - proposedNeedsPct - proposedSavingsPct;

        result.Status = RecommendationStatus.Adjustable;
        result.Proposed = new IncomeAllocationEntryDto
        {
            EffectiveMonth = MonthKey(DateTime.UtcNow.AddMonths(1)),
            MonthlyIncome = income,
            NeedsPct = proposedNeedsPct,
            WantsPct = proposedWantsPct,
            SavingsPct = proposedSavingsPct
        };
        result.ProposedNeedsCap = Math.Round(income * proposedNeedsPct / 100m, 2);
        result.ProposedWantsCap = Math.Round(income * proposedWantsPct / 100m, 2);
        result.ProposedSavingsCap = Math.Round(income * proposedSavingsPct / 100m, 2);

        return result;
    }

    // ICT (UTC+7), matching BudgetService.ResolveMonthWindow's convention for "current month".
    internal static string MonthKey(DateTime utcNow)
    {
        var local = utcNow.AddHours(7);
        return $"{local.Year:D4}-{local.Month:D2}";
    }

    /// <summary>
    /// Validates and normalizes a caller-supplied <c>yyyy-MM</c> string (same format rule as
    /// <c>BudgetService.ResolveMonthWindow</c>, same error message for consistency). Pure/no I/O.
    /// </summary>
    internal static string NormalizeMonth(string month)
    {
        var parts = month.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var parsedMonth) ||
            year < 1 ||
            parsedMonth is < 1 or > 12)
        {
            throw new ValidationException("Month must use yyyy-MM format.");
        }

        return $"{year:D4}-{parsedMonth:D2}";
    }

    /// <summary>
    /// Latest row with <c>EffectiveMonth &lt;= month</c> (carry-forward), or null if the customer
    /// has no history row that old yet. Pure/no I/O so it's unit-testable without a database.
    /// </summary>
    internal static IncomeAllocationSetting? ResolveEffectiveRow(
        IReadOnlyList<IncomeAllocationSetting> rows, string month)
        => rows
            .Where(r => string.CompareOrdinal(r.EffectiveMonth, month) <= 0)
            .OrderByDescending(r => r.EffectiveMonth, StringComparer.Ordinal)
            .FirstOrDefault();

    private static IncomeAllocationEntryDto ToDto(IncomeAllocationSetting entry) => new()
    {
        EffectiveMonth = entry.EffectiveMonth,
        MonthlyIncome = entry.MonthlyIncome,
        NeedsPct = entry.NeedsPct,
        WantsPct = entry.WantsPct,
        SavingsPct = entry.SavingsPct
    };
}
