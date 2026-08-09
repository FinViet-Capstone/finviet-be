namespace FinViet.Application.DTOs.Wallets;

/// <summary>
/// Rút tiền: money out of a SePay-linked bank wallet (<see cref="FromWalletId"/>), recorded as
/// an <c>expense</c> on that wallet. <see cref="ToWalletId"/> is the optional receiving wallet
/// (ví nhận tiền rút) — when set, the amount is credited to it as income; when null, the withdrawn
/// cash simply leaves the tracked wallets. The receiving wallet cannot be a SePay-linked wallet.
/// </summary>
public class WithdrawWalletRequest
{
    public Guid FromWalletId { get; set; }
    public Guid? ToWalletId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
