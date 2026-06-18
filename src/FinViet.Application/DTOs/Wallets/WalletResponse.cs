namespace FinViet.Application.DTOs.Wallets;

public class WalletResponse
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string Name
    {
        get => WalletName;
        set => WalletName = value;
    }
    public string WalletName { get; set; } = string.Empty;
    public string Type
    {
        get => WalletType;
        set => WalletType = value;
    }
    public string WalletType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
