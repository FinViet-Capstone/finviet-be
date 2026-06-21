using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>Maps to table <c>category_requests</c> (v2.1).</summary>
public class CategoryRequest
{
    public Guid RequestId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Column <c>requested_name</c>.</summary>
    public string CategoryName { get; set; } = null!;

    /// <summary>Column <c>type</c> (enum category_type).</summary>
    public CategoryType Type { get; set; }

    /// <summary>Column <c>suggested_bucket_id</c> (FK buckets.id) — replaces the legacy expense_class.</summary>
    public string? SuggestedBucketId { get; set; }

    public string? Note { get; set; }

    /// <summary>Column <c>status</c> (enum category_request_status).</summary>
    public CategoryRequestStatus Status { get; set; } = CategoryRequestStatus.Pending;

    /// <summary>Column <c>reviewed_by_admin_id</c>.</summary>
    public Guid? ReviewedBy { get; set; }

    public string? ReviewNote { get; set; }
    public string? CreatedCategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
