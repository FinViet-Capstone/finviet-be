namespace FinViet.Infrastructure.Services;

/// <summary>Business dates are Vietnam calendar days (Asia/Ho_Chi_Minh, UTC+7) regardless of the
/// server's own time zone. Reads time through <see cref="TimeProvider"/> so callers stay testable.</summary>
internal static class VietnamClock
{
    internal static readonly TimeZoneInfo Zone = ResolveZone();

    internal static DateTime Now(TimeProvider time) =>
        TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, Zone);

    internal static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(Now(time));

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN+7", TimeSpan.FromHours(7), "Vietnam (+7)", "Vietnam (+7)");
    }
}
