namespace FinViet.Infrastructure.Persistence.Entities;

public class CategoryRequest
{
    public Guid RequestId { get; set; }
    public Guid CustomerId { get; set; }
    public string CategoryName { get; set; } = null!;
    public string Type { get; set; } = null!;

    /// <summary>
    /// Maps to <c>category_requests.suggested_bucket_id</c> — a FK to <c>buckets(id)</c> whose
    /// values are the lowercase slugs <c>needs</c> / <c>wants</c> / <c>savings</c>.
    /// </summary>
    public string? SuggestedBucketId { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public string? CreatedCategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
