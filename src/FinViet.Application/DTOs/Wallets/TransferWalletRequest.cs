namespace FinViet.Application.DTOs.Wallets;

public class TransferWalletRequest
{
    public Guid FromWalletId { get; set; }
    public Guid ToWalletId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
