namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>entry_method</c> (manual, photo, sms_paste, csv_import, sepay_sync, finverse_sync).</summary>
public enum EntryMethod
{
    Manual,
    Photo,
    SmsPaste,
    CsvImport,
    SepaySync,

    /// <summary>
    /// Legacy label from the removed Finverse integration. Postgres cannot drop an enum value, so
    /// the member is retained purely so Npgsql can still read any row written before V20; nothing
    /// in the application produces it.
    /// </summary>
    FinverseSync
}
