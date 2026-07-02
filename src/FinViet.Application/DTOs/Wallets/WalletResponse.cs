namespace FinViet.Application.DTOs.Wallets;

public class WalletResponse
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string? FinverseAccountId { get; set; }
    public string? InstitutionName { get; set; }
    public string? AccountMask { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
