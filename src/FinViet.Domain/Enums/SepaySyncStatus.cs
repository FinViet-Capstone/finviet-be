namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>sepay_sync_status</c> (ok, syncing, error).</summary>
public enum SepaySyncStatus
{
    Ok,
    Syncing,
    Error
}
