namespace FinViet.Infrastructure.Services;

/// <summary>Helpers for building UTC day boundaries from DateOnly. Npgsql maps timestamptz columns
/// (e.g. transaction.transaction_date) and requires DateTimeKind.Utc — DateOnly.ToDateTime yields
/// Kind=Unspecified, which Npgsql rejects.</summary>
internal static class DateRange
{
    public static DateTime StartUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    public static DateTime EndUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
}
