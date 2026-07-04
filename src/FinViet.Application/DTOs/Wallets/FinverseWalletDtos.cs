namespace FinViet.Application.DTOs.Wallets;

public sealed class CreateFinverseLinkRequest
{
    public string? InstitutionId { get; set; }

    public string Language { get; set; } = "en";

    public string UiMode { get; set; } = "redirect";
}

public sealed class CompleteFinverseLinkRequest
{
    public string Code { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Optional subset of accounts to link. When null/empty every usable account returned by
    /// Finverse becomes (or restores) a wallet; when set, only matching accounts do — so a client
    /// can link a single account instead of all of them. Each entry may be a Finverse account id,
    /// the account name (case-insensitive, e.g. "Bitcoin"), or the masked account number —
    /// account ids are minted per login identity, so name/mask is the practical selector.
    /// </summary>
    public List<string>? Accounts { get; set; }
}

public sealed class FinverseLinkTokenResponse
{
    public string LinkUrl { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

public sealed class FinverseLinkResult
{
    public string LoginIdentityId { get; set; } = string.Empty;

    public IReadOnlyList<WalletResponse> Wallets { get; set; } = Array.Empty<WalletResponse>();
}

public sealed class FinverseWalletSyncResponse
{
    public Guid WalletId { get; set; }

    public decimal Balance { get; set; }

    public int TransactionsCreated { get; set; }

    public int TransactionsUpdated { get; set; }

    public int PendingTransactionsSkipped { get; set; }

    public DateTime SyncedAt { get; set; }
}
