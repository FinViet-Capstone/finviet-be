using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CustomerCategory
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string CategoryId { get; set; } = null!;

    public string BucketId { get; set; } = null!;

    /// <summary>Postgres enum <c>category_source</c> (persona/system).</summary>
    public string Source { get; set; } = "system";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Customer? Customer { get; set; }
}
