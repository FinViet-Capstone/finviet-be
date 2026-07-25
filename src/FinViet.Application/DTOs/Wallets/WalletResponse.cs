namespace FinViet.Application.DTOs.Wallets;

public class WalletResponse
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string WalletName { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
    public decimal Balance { get; set; }

    /// <summary>SePay bank account id — only set on <c>sepay_linked</c> wallets (0 for static links).</summary>
    public int? SepayBankAccountId { get; set; }

    /// <summary>Bank short name reported by SePay (e.g. "MBBank").</summary>
    public string? InstitutionName { get; set; }

    /// <summary>Bank account number, masked to the last 4 digits.</summary>
    public string? AccountMask { get; set; }

    /// <summary>"oauth" or "static" for a linked wallet; null for a manual wallet.</summary>
    public string? AuthMode { get; set; }

    public DateTime? LastSyncedAt { get; set; }
}
