namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>entry_method</c> (manual, photo, sms_paste, csv_import, sepay_sync).</summary>
public enum EntryMethod
{
    Manual,
    Photo,
    SmsPaste,
    CsvImport,
    SepaySync
}
