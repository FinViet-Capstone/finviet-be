namespace FinViet.Infrastructure.Persistence.Entities;

public class CategoryRequest
{
    public Guid RequestId { get; set; }
    public Guid CustomerId { get; set; }
    public string CategoryName { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? ExpenseClass { get; set; }
    public string? Note { get; set; }

    /// <summary>PENDING, APPROVED, or REJECTED.</summary>
    public string Status { get; set; } = "PENDING";

    public Guid? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? CreatedCategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
