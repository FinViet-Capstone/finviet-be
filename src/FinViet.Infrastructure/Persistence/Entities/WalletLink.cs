using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>SePay link metadata for a wallet (1:1 with wallet). Maps to table <c>wallet_links</c>.</summary>
public class WalletLink
{
    public Guid WalletId { get; set; }

    public string? SepayToken { get; set; }

    public string? SepayBankName { get; set; }

    public string? SepayAccountId { get; set; }

    public string? SepayAccountMask { get; set; }

    public DateTime? SepayLastSyncAt { get; set; }

    /// <summary>Last SePay transaction id pulled — used as the since_id polling cursor.</summary>
    public string? SepayLastSyncedTxnId { get; set; }

    public SepaySyncStatus SepaySyncStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Wallet Wallet { get; set; } = null!;
}
