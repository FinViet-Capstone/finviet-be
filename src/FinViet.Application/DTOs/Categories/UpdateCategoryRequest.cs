namespace FinViet.Application.DTOs.Categories;

public class UpdateCategoryRequest
{
    public string? CategoryName { get; set; }
    public string? NameVi { get; set; }
    public string? NameEn { get; set; }
    public string? Type { get; set; }
    public bool? IsMandatory { get; set; }
    public string? ExpenseClass { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public int? SortOrder { get; set; }
}
