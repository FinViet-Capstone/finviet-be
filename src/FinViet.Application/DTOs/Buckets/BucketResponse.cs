namespace FinViet.Application.DTOs.Buckets;

public class BucketResponse
{
    public string Id { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int? SortOrder { get; set; }
    public bool IsLocked { get; set; }
}
