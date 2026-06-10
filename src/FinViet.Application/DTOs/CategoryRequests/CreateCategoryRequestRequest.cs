namespace FinViet.Application.DTOs.CategoryRequests;

public class CreateCategoryRequestRequest
{
    public string CategoryName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ExpenseClass { get; set; }
    public string? Note { get; set; }
}
