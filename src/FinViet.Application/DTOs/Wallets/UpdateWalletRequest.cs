namespace FinViet.Application.DTOs.Wallets;

public class UpdateWalletRequest
{
    public string? Name { get; set; }
    public string? WalletName { get; set; }
    public string? WalletType { get; set; }

    public string? EffectiveName => string.IsNullOrWhiteSpace(Name) ? WalletName : Name;
}
