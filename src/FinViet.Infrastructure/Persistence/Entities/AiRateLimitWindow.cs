namespace FinViet.Infrastructure.Persistence.Entities;

public class AiRateLimitWindow
{
    public Guid CustomerId { get; set; }

    public string Feature { get; set; } = null!;

    public string WindowType { get; set; } = null!;

    public DateTime WindowStart { get; set; }

    public int RequestCount { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
