namespace FinViet.Application.DTOs.CategoryCorrections;

public class CategoryCorrectionQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? CategoryId { get; set; }
    public DateTime? CreatedAtFrom { get; set; }
    public DateTime? CreatedAtTo { get; set; }
}
