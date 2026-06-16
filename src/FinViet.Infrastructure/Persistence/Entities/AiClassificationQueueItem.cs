using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiClassificationQueueItem
{
    public Guid QueueId { get; set; }

    public Guid TransactionId { get; set; }

    public Guid CustomerId { get; set; }

    public string RawInput { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTime EnqueuedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public virtual Transaction? Transaction { get; set; }

    public virtual Customer? Customer { get; set; }
}
