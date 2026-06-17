using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class UserCategoryBucket
{
    public Guid CustomerId { get; set; }

    public string CategoryId { get; set; } = null!;

    public string Bucket { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual Category? Category { get; set; }
}
