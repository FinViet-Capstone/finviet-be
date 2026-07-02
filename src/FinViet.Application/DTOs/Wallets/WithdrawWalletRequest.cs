namespace FinViet.Application.DTOs.Wallets;

/// <summary>
/// Rút tiền: move money out of a Finverse-linked wallet into a basic wallet.
/// <see cref="ToWalletId"/> is optional — when the customer has exactly one basic wallet it is
/// chosen automatically; with several, the API returns 422 "multiple_basic_wallets" so the client
/// can prompt for an explicit choice.
/// </summary>
public class WithdrawWalletRequest
{
    public Guid FromWalletId { get; set; }
    public Guid? ToWalletId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
