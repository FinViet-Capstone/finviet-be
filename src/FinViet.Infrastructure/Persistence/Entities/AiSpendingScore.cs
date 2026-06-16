using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiSpendingScore
{
    public Guid ScoreId { get; set; }

    public Guid CustomerId { get; set; }

    public string PeriodType { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public decimal FinalScore { get; set; }

    public decimal? SpikeScore { get; set; }

    public decimal? BudgetScore { get; set; }

    public decimal? SavingsScore { get; set; }

    public string? WeightsJson { get; set; }

    public string? ColorBadge { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
