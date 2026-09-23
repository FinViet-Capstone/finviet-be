namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Only expenses dated in the current or previous calendar month (Asia/Ho_Chi_Minh) are queued for
/// background categorization; older imported history is left for the user to retry.
/// </summary>
public static class SepayCategorizationWindow
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static bool IsInWindow(DateTime transactionDateUtc, DateTime utcNow)
    {
        var date = ToVietnam(transactionDateUtc);
        var now = ToVietnam(utcNow);
        var previousMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        return date >= previousMonth && date < new DateTime(now.Year, now.Month, 1).AddMonths(1);
    }

    /// <summary>First day of the Vietnam-local month containing <paramref name="utc"/>.</summary>
    public static DateOnly MonthOf(DateTime utc)
    {
        var local = ToVietnam(utc);
        return new DateOnly(local.Year, local.Month, 1);
    }

    private static DateTime ToVietnam(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(VietnamOffset);
}
