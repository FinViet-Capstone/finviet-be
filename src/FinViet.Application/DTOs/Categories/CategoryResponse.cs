namespace FinViet.Application.DTOs.Categories;

public class CategoryResponse
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public string? ExpenseClass { get; set; }
}
