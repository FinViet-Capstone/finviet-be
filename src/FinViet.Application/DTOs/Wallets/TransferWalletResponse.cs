namespace FinViet.Application.DTOs.Wallets;

public class TransferWalletResponse
{
    public Guid FromWalletId { get; set; }
    public Guid ToWalletId { get; set; }
    public decimal FromWalletBalance { get; set; }
    public decimal ToWalletBalance { get; set; }
}
