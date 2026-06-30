namespace FinViet.Application.DTOs.Wallets;

public class WalletListResponse
{
    public decimal TotalBalance { get; set; }

    public IReadOnlyList<WalletResponse> Wallets { get; set; } = Array.Empty<WalletResponse>();
}
