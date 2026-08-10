using FinViet.Application.Exceptions;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Application.UnitTests;

// TC-INCALLOC-01..08 — pure resolver logic behind the income-allocation history feature
// (be-revamp.md item 2). No database/mocking needed: both methods under test are pure.
public class IncomeAllocationServiceTests
{
    private static IncomeAllocationSetting Row(string effectiveMonth, decimal income = 0m)
        => new() { EffectiveMonth = effectiveMonth, MonthlyIncome = income };

    [Fact]
    public void ResolveEffectiveRow_NoRows_ReturnsNull()
    {
        var result = IncomeAllocationService.ResolveEffectiveRow(Array.Empty<IncomeAllocationSetting>(), "2026-07");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveEffectiveRow_SingleRowExactMonth_ReturnsThatRow()
    {
        var row = Row("2026-07");
        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { row }, "2026-07");
        Assert.Same(row, result);
    }

    [Fact]
    public void ResolveEffectiveRow_SingleRowBeforeMonth_CarriesForward()
    {
        var row = Row("2026-05");
        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { row }, "2026-07");
        Assert.Same(row, result);
    }

    [Fact]
    public void ResolveEffectiveRow_SingleRowAfterMonth_ReturnsNull()
    {
        // A row scheduled for a future month must never apply to a past/current-month query —
        // this is the core guarantee the whole feature exists for.
        var row = Row("2026-09");
        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { row }, "2026-07");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveEffectiveRow_MultipleRows_PicksLatestNotExceedingMonth()
    {
        var jan = Row("2026-01", 10m);
        var may = Row("2026-05", 20m);
        var sep = Row("2026-09", 30m); // future relative to the query month — must be ignored

        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { jan, may, sep }, "2026-07");

        Assert.Same(may, result);
    }

    [Fact]
    public void ResolveEffectiveRow_RowInsertionOrderDoesNotMatter()
    {
        var may = Row("2026-05", 20m);
        var jan = Row("2026-01", 10m);

        // Deliberately inserted "backwards" — result must still be the chronologically latest.
        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { may, jan }, "2026-07");

        Assert.Same(may, result);
    }

    [Fact]
    public void ResolveEffectiveRow_ExactBoundaryMonth_IsInclusive()
    {
        var current = Row("2026-07", 50m);
        var future = Row("2026-08", 99m);

        var result = IncomeAllocationService.ResolveEffectiveRow(new[] { current, future }, "2026-07");

        Assert.Same(current, result);
    }

    [Theory]
    [InlineData("2026-07-26T10:00:00Z", "2026-07")] // 17:00 ICT, same day/month
    [InlineData("2026-07-26T16:30:00Z", "2026-07")] // 23:30 ICT, same day/month
    [InlineData("2026-07-26T17:30:00Z", "2026-07")] // 00:30 ICT next day, still July
    [InlineData("2026-12-31T18:00:00Z", "2027-01")] // 01:00 ICT Jan 1 — year rollover
    public void MonthKey_UsesIctOffset(string utcIso, string expectedMonthKey)
    {
        var utc = DateTime.Parse(utcIso).ToUniversalTime();
        var key = IncomeAllocationService.MonthKey(utc);

        Assert.Equal(expectedMonthKey, key);
    }

    // TC-INCALLOC-09 — arbitrary-month lookup (docs/10-08-2026-be-todos.md §3)
    [Theory]
    [InlineData("2026-1", "2026-01")]
    [InlineData(" 2026-07 ", "2026-07")]
    [InlineData("2026-12", "2026-12")]
    public void NormalizeMonth_ValidFormats_ReturnsZeroPaddedMonth(string input, string expected)
    {
        Assert.Equal(expected, IncomeAllocationService.NormalizeMonth(input));
    }

    // TC-INCALLOC-10
    [Theory]
    [InlineData("2026")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("abcd-ef")]
    [InlineData("07-2026")]
    public void NormalizeMonth_InvalidFormat_ThrowsValidationException(string input)
    {
        Assert.Throws<ValidationException>(() => IncomeAllocationService.NormalizeMonth(input));
    }

    // TC-INCALLOC-11
    [Fact]
    public async Task GetSummaryAsync_WithMonth_ResolvesCurrentForThatMonthAndPendingIsNull()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            CustomerId = customerId,
            Email = "test@finviet.local",
            FullName = "Test Customer",
            IsActive = true
        });
        db.IncomeAllocationSettings.AddRange(
            new IncomeAllocationSetting
            {
                Id = Guid.NewGuid(), CustomerId = customerId, EffectiveMonth = "2026-01",
                MonthlyIncome = 10_000_000m, NeedsPct = 50, WantsPct = 30, SavingsPct = 20
            },
            // A "next real month" draft that must NOT leak into Pending when an explicit
            // historical month is queried — Pending is only meaningful for the real current month.
            new IncomeAllocationSetting
            {
                Id = Guid.NewGuid(), CustomerId = customerId,
                EffectiveMonth = IncomeAllocationService.MonthKey(DateTime.UtcNow.AddMonths(1)),
                MonthlyIncome = 99_000_000m, NeedsPct = 10, WantsPct = 10, SavingsPct = 80
            });
        await db.SaveChangesAsync();

        var result = await new IncomeAllocationService(db).GetSummaryAsync(customerId, "2026-06");

        Assert.Equal("2026-01", result.Current.EffectiveMonth); // carried forward from January
        Assert.Equal(10_000_000m, result.Current.MonthlyIncome);
        Assert.Null(result.Pending);
    }

    // TC-INCALLOC-12
    [Fact]
    public async Task GetSummaryAsync_WithInvalidMonth_ThrowsValidationException()
    {
        await using var db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new IncomeAllocationService(db).GetSummaryAsync(Guid.NewGuid(), "not-a-month"));
    }
}
