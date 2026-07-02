namespace FinViet.Application.DTOs.Wallets;

/// <summary>
/// Result of a withdrawal. The withdrawn amount is recorded as an <c>expense</c> on the source
/// (Finverse-linked) wallet — money out, counted as spending. When a receiving wallet is given,
/// the amount is credited to it and the <c>To*</c> fields are populated; they stay null when the
/// cash left the tracked wallets (no destination).
/// </summary>
public class WithdrawWalletResponse
{
    public Guid FromWalletId { get; set; }
    public decimal FromWalletBalance { get; set; }
    public Guid? ToWalletId { get; set; }
    public decimal? ToWalletBalance { get; set; }
}
