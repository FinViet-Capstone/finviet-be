namespace FinViet.Application.DTOs.SavingGoals;

public class UpdateSavingGoalRequest
{
    public string? GoalName { get; set; }
    public decimal? TargetAmount { get; set; }
    public DateOnly? Deadline { get; set; }
}
