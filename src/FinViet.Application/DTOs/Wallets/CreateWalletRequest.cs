namespace FinViet.Application.DTOs.Wallets;

public class CreateWalletRequest
{
    public string? Name { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string WalletType { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }

    public string EffectiveName => string.IsNullOrWhiteSpace(Name) ? WalletName : Name!;

    public string EffectiveType => string.IsNullOrWhiteSpace(Type) ? WalletType : Type!;
}
