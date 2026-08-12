namespace FinViet.Infrastructure.Persistence.Entities;

public class AiUsageEvent
{
    public Guid Id { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? SessionId { get; set; }

    public string Feature { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string? Model { get; set; }

    public string Outcome { get; set; } = null!;

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public int? TotalTokens { get; set; }

    public int? LatencyMs { get; set; }

    public string? ProviderRequestId { get; set; }

    public string Metadata { get; set; } = "{}";

    public DateTime OccurredAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
