namespace FinViet.Infrastructure.Persistence.Entities;

public class AiCustomerPreference
{
    public Guid CustomerId { get; set; }

    public string CategorizationMode { get; set; } = "suggest_only";

    public decimal AutoCategorizationThreshold { get; set; } = 0.85m;

    public bool DefaultHistoryEnabled { get; set; } = true;

    public bool WeeklyReportEnabled { get; set; } = true;

    public bool ShareBalances { get; set; } = true;

    public bool ShareTransactions { get; set; } = true;

    public bool ShareBudgets { get; set; } = true;

    public bool ShareGoals { get; set; } = true;

    public bool ShareReports { get; set; } = true;

    public bool RagEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
