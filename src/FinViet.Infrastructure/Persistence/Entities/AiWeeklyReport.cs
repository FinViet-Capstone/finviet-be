using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiWeeklyReport
{
    public Guid ReportId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid? ScoreId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string Narrative { get; set; } = null!;

    public DateTime GeneratedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual AiSpendingScore? Score { get; set; }
}
