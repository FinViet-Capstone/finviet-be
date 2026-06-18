using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CustomerCategory
{
    public Guid CustomerId { get; set; }

    public string CategoryId { get; set; } = null!;

    public string BucketId { get; set; } = null!;

    public string Source { get; set; } = "system";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Customer? Customer { get; set; }
}
