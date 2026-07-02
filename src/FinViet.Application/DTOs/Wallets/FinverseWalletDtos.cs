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
