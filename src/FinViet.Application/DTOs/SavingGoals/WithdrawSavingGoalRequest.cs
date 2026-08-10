namespace FinViet.Application.DTOs.SavingGoals;

public class WithdrawSavingGoalRequest
{
    public decimal Amount { get; set; }

    /// <summary>
    /// Destination wallet the withdrawn amount is credited to. Required on every call — a goal
    /// has no static withdrawal wallet, matching the per-action wallet choice on contribute.
    /// Must not be a sepay_linked wallet.
    /// </summary>
    public Guid WalletId { get; set; }

    public string? Note { get; set; }
}
