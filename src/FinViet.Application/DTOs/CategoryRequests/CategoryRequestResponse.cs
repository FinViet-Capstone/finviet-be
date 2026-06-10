namespace FinViet.Application.DTOs.CategoryRequests;

public class CategoryRequestResponse
{
    public Guid RequestId { get; set; }
    public Guid CustomerId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ExpenseClass { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
    public Guid? CreatedCategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
