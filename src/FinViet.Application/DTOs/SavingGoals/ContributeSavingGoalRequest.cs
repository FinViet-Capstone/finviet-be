namespace FinViet.Application.DTOs.SavingGoals;

public class ContributeSavingGoalRequest
{
    public decimal Amount { get; set; }

    /// <summary>
    /// Source wallet the contribution is deducted from. When set, the amount is withdrawn
    /// from this wallet and an expense transaction is recorded. When null, the goal's own
    /// funding wallet (if any) is used; otherwise the contribution only increments progress.
    /// </summary>
    public Guid? FundingWalletId { get; set; }
}
