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

    /// <summary>Current-month effective allocation plus the next-month draft, if one is scheduled.</summary>
    Task<IncomeAllocationSummaryDto> GetSummaryAsync(
        Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the entry for next calendar month. Calling this again before rollover revises the
    /// same draft — it never creates a second pending row or touches the current/past entry.
    /// </summary>
    Task<IncomeAllocationEntryDto> ScheduleNextMonthAsync(
        Guid customerId,
        decimal monthlyIncome,
        int needsPct,
        int wantsPct,
        int savingsPct,
        CancellationToken cancellationToken = default);
}
