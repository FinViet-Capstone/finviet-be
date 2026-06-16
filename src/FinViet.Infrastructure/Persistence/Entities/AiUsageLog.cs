using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiUsageLog
{
    public Guid UsageId { get; set; }

    public Guid CustomerId { get; set; }

    public string Feature { get; set; } = null!;

    public DateTime CalledAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
