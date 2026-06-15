namespace FinViet.Application.DTOs.Categories;

public class UpdateCategoryRequest
{
    public string? CategoryName { get; set; }
    public string? Type { get; set; }
    public bool? IsMandatory { get; set; }
    public string? ExpenseClass { get; set; }
}
