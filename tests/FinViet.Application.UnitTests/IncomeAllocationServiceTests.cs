using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;

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
}
