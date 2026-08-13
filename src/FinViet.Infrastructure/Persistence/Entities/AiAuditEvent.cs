namespace FinViet.Infrastructure.Persistence.Entities;

public class AiAuditEvent
{
    public Guid Id { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? SessionId { get; set; }

    public string ActorType { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public Guid? CorrelationId { get; set; }

    public string Metadata { get; set; } = "{}";

    public DateTime OccurredAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
