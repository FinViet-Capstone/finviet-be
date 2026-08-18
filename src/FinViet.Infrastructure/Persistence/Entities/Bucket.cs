namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>Lookup table for spending buckets (needs/wants/savings). Maps to table <c>buckets</c>.</summary>
public class Bucket
{
    public string Id { get; set; } = null!;

    public string NameVi { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    public string? Color { get; set; }

    public string? Icon { get; set; }

    public int? SortOrder { get; set; }

    public bool IsLocked { get; set; }

    /// <summary>System-wide default allocation percentage applied to new customers at registration.</summary>
    public decimal? DefaultPct { get; set; }
}
