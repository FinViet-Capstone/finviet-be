using FinViet.Infrastructure.Services.Background;

namespace FinViet.Application.UnitTests;

public class SepayCategorizationWindowTests
{
    // 2026-09-24 10:00 in Vietnam.
    private static readonly DateTime Now = new(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(2026, 9, 24, 3, true)]   // today
    [InlineData(2026, 9, 1, 0, true)]    // current month
    [InlineData(2026, 8, 31, 17, true)]  // 2026-09-01 00:00 VN, current month
    [InlineData(2026, 8, 31, 16, true)]  // 2026-08-31 23:00 VN, previous month
    [InlineData(2026, 8, 1, 0, true)]    // previous month start
    [InlineData(2026, 7, 31, 16, false)] // 2026-07-31 23:00 VN, two months back
    [InlineData(2025, 9, 24, 3, false)]  // a year ago
    [InlineData(2026, 10, 1, 3, false)]  // next month
    public void IsInWindow_UsesVietnamCalendarMonths(int year, int month, int day, int hour, bool expected)
    {
        var date = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, SepayCategorizationWindow.IsInWindow(date, Now));
    }

    [Fact]
    public void IsInWindow_AtVietnamMonthBoundary_UsesLocalTime()
    {
        // 2026-10-01 00:30 VN is still September in UTC but already October in Vietnam.
        var now = new DateTime(2026, 9, 30, 17, 30, 0, DateTimeKind.Utc);

        Assert.True(SepayCategorizationWindow.IsInWindow(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), now));
        Assert.False(SepayCategorizationWindow.IsInWindow(new DateTime(2026, 8, 31, 16, 0, 0, DateTimeKind.Utc), now));
    }

    [Fact]
    public void MonthOf_ReturnsFirstDayOfVietnamMonth()
    {
        Assert.Equal(
            new DateOnly(2026, 9, 1),
            SepayCategorizationWindow.MonthOf(new DateTime(2026, 8, 31, 17, 0, 0, DateTimeKind.Utc)));
    }
}
