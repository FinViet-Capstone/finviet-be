namespace FinViet.Application.DTOs.SavingGoals;

public class CreateSavingGoalRequest
{
    public string GoalName { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateOnly? Deadline { get; set; }
    public decimal? InitialAmount { get; set; }
    public Guid? FundingWalletId { get; set; }
}
