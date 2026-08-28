using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface IIncomeAllocationService
{
    /// <summary>
    /// Resolves the allocation effective for <paramref name="month"/> (<c>yyyy-MM</c>): the latest
    /// history row with <c>EffectiveMonth &lt;= month</c>, carried forward, falling back to the
    /// customer's onboarding-time <c>Customer</c> columns if no history row exists yet.
    /// </summary>
    Task<IncomeAllocationEntryDto> GetEffectiveAsync(
        Guid customerId, string month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Current-month effective allocation plus the next-month draft, if one is scheduled.
    /// When <paramref name="month"/> (<c>yyyy-MM</c>) is given, <c>Current</c> resolves for that
    /// arbitrary month instead and <c>Pending</c> is always null (it only means "next real
    /// calendar month's draft", which isn't meaningful relative to an arbitrary queried month).
    /// Throws a 400 validation error if <paramref name="month"/> isn't valid <c>yyyy-MM</c>.
    /// </summary>
    Task<IncomeAllocationSummaryDto> GetSummaryAsync(
        Guid customerId, string? month = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the entry for next calendar month. Calling this again before rollover revises the
    /// same draft — it never creates a second pending row or touches the current/past entry.
    /// </summary>
    Task<IncomeAllocationEntryDto> ScheduleNextMonthAsync(
        Guid customerId,
        decimal monthlyIncome,
        decimal needsPct,
        decimal wantsPct,
        decimal savingsPct,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares what the customer's active saving goals need each month against what their own
    /// Savings bucket actually allocates, and proposes a rebalanced split when the goals outrun
    /// it. Read-only — nothing is written. <paramref name="month"/> (<c>yyyy-MM</c>) defaults to
    /// the current month; throws a 400 validation error if it isn't valid <c>yyyy-MM</c>.
    /// </summary>
    Task<SavingsPlanRecommendationDto> GetSavingsPlanRecommendationAsync(
        Guid customerId, string? month = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the recommendation server-side and, when it is actionable, schedules the
    /// proposed split for next month through <see cref="ScheduleNextMonthAsync"/>. Throws a
    /// <c>BusinessRuleException</c> (422) carrying the recommendation's status as its code when
    /// there is nothing to apply, so the client never has to trust a stale proposal it is
    /// holding.
    /// </summary>
    Task<IncomeAllocationEntryDto> ApplySavingsPlanRecommendationAsync(
        Guid customerId, CancellationToken cancellationToken = default);
}
