namespace FinViet.Application.UnitTests.Infrastructure;

/// <summary>A <see cref="TimeProvider"/> frozen at one instant, settable between steps.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <summary>Noon (UTC+7) on <paramref name="vietnamDate"/>, inside the reminder window.</summary>
    public static FixedTimeProvider AtVietnamNoon(DateOnly vietnamDate) =>
        new(new DateTimeOffset(vietnamDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7)));
}
