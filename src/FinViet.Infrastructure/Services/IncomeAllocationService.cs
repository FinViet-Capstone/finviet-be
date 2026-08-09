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
        int needsPct,
        int wantsPct,
        int savingsPct,
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
