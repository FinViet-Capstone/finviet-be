namespace FinViet.Application.DTOs.Wallets;

public class UpdateWalletRequest
{
    public string? WalletName { get; set; }
    public string? WalletType { get; set; }
    public decimal? Balance { get; set; }
}
