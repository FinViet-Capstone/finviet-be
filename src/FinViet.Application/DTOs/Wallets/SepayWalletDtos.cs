namespace FinViet.Application.DTOs.Wallets;

/// <summary>
/// Request to link a SePay bank account after the user completes the OAuth2 authorization-code flow.
/// The frontend opens my.sepay.vn/oauth/authorize in a WebView, receives the code on redirect,
/// and sends it here.
/// </summary>
public sealed class LinkSepayAccountRequest
{
    /// <summary>OAuth2 authorization code from the SePay redirect.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// SePay bank account id to link (from GET /api/v1/bank-accounts). When null, the first
    /// active bank account is linked automatically.
    /// </summary>
    public int? BankAccountId { get; set; }
}

/// <summary>Response after successfully linking a SePay bank account.</summary>
public sealed class SepayLinkResult
{
    public IReadOnlyList<WalletResponse> Wallets { get; set; } = Array.Empty<WalletResponse>();

    public int TransactionsSynced { get; set; }
}

/// <summary>A SePay bank account available for linking.</summary>
public sealed class SepayBankAccountResponse
{
    public int Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountHolderName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public string BankShortName { get; set; } = string.Empty;

    public string BankCode { get; set; } = string.Empty;

    public string? BankIconUrl { get; set; }
}

/// <summary>Response after syncing a SePay-linked wallet.</summary>
public sealed class SepayWalletSyncResponse
{
    public Guid WalletId { get; set; }

    public decimal Balance { get; set; }

    public int TransactionsCreated { get; set; }

    public int TransactionsUpdated { get; set; }

    public DateTime SyncedAt { get; set; }
}

/// <summary>Request body for GET SePay bank accounts (uses OAuth code).</summary>
public sealed class SepayBankAccountsRequest
{
    /// <summary>OAuth2 authorization code (single-use, exchanged for a temporary token).</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Request to link a bank account using a personal SePay User API token
/// (my.sepay.vn → API Access). No OAuth — the token is validated, then stored encrypted.
/// </summary>
public sealed class LinkSepayTokenRequest
{
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional bank account number to restrict the wallet to one account when the
    /// SePay token exposes several. When null, all transactions from the token are imported.
    /// </summary>
    public string? AccountNumber { get; set; }
}
