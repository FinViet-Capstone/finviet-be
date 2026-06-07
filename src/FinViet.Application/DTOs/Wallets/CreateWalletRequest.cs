namespace FinViet.Application.DTOs.Wallets;

public class CreateWalletRequest
{
    public string WalletName { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
}
