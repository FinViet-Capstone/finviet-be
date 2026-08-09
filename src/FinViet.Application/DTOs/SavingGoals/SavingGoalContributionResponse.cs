namespace FinViet.Application.DTOs.SavingGoals;

public class SavingGoalContributionResponse
{
    public Guid ContributionId { get; set; }
    public Guid GoalId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>"contribution" | "withdrawal" — direction; Amount is always stored positive.</summary>
    public string Type { get; set; } = "contribution";

    public DateTime ContributedAt { get; set; }
    public string? Note { get; set; }
    public Guid? TransactionId { get; set; }
}
