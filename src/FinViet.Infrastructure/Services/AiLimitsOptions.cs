namespace FinViet.Infrastructure.Services;

/// <summary>Binds the "AiLimits" section of appsettings.json.</summary>
public class AiLimitsOptions
{
    public const string SectionName = "AiLimits";

    public int PerUserPerDay { get; set; } = 100;

    public int PerUserPerMinute { get; set; } = 6;

    /// <summary>Separate, higher allowance for bulk SMS/CSV import categorization
    /// (feature "classification_batch") — a single large statement import can legitimately need
    /// far more classification calls in one request than the per-transaction limits above allow.</summary>
    public int BulkImportPerMinute { get; set; } = 100;

    public int BulkImportPerDay { get; set; } = 1000;

    /// <summary>Separate, higher allowance for interactive single-row category preview
    /// (feature "classification_preview" — photo extraction and the mobile "suggest category"
    /// button) — this is human-paced, bursty tapping, not the occasional automatic per-transaction
    /// call the standard limits above are sized for, so it needs its own tier rather than sharing
    /// the 6/minute standard cap.</summary>
    public int PreviewPerMinute { get; set; } = 30;

    public int PreviewPerDay { get; set; } = 300;
}
