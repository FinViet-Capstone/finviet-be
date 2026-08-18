namespace FinViet.Application.DTOs.Buckets;

public class UpdateBucketRequest
{
    public string? NameVi { get; set; }
    public string? NameEn { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int? SortOrder { get; set; }
    public decimal? DefaultPct { get; set; }
}
