namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// One customer's income/50-30-20 allocation as of a given calendar month. Rows are only ever
/// inserted for the *next* calendar month (see <c>IIncomeAllocationService.ScheduleNextMonthAsync</c>)
/// and never rewritten for a month that has already started, so past/current-month reads are stable
/// even after a later edit. When no row exists yet for a customer, <c>Customer.MonthlyIncomeExpected</c>/
/// <c>NeedsPct</c>/<c>WantsPct</c>/<c>SavingsPct</c> (set at onboarding) are the fallback.
/// </summary>
public sealed class IncomeAllocationSetting
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>First-of-month key, format <c>yyyy-MM</c> (matches <c>BudgetService</c>'s month-window key).</summary>
    public string EffectiveMonth { get; set; } = string.Empty;

    public decimal MonthlyIncome { get; set; }

    public decimal NeedsPct { get; set; }

    public decimal WantsPct { get; set; }

    public decimal SavingsPct { get; set; }

    public DateTime CreatedAt { get; set; }

    public Customer Customer { get; set; } = null!;
}
