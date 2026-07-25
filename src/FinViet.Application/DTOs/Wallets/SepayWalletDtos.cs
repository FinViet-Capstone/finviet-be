namespace FinViet.Application.DTOs.Wallets;

/// <summary>
/// Everything the client needs to start the OAuth2 flow: open <see cref="AuthorizeUrl"/> in a
/// WebView and send the returned <c>code</c> back together with <see cref="State"/>.
/// </summary>
public sealed class SepayAuthorizeUrlResponse
{
    public string AuthorizeUrl { get; set; } = string.Empty;

    /// <summary>Signed, single-customer, expiring CSRF token. Echo it back on link.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Request to link a SePay bank account after the user completes the OAuth2 authorization-code flow.
/// The frontend opens the URL from <c>GET /api/wallets/sepay/authorize-url</c> in a WebView,
/// receives the code on redirect, and sends it here.
/// </summary>
public sealed class LinkSepayAccountRequest
{
    /// <summary>OAuth2 authorization code from the SePay redirect.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The <c>state</c> issued by <c>GET /api/wallets/sepay/authorize-url</c>. Optional for
    /// backwards compatibility, but when present it must be valid and belong to the caller.
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// SePay bank account id to link (from <c>POST /api/wallets/sepay/bank-accounts</c>). When
    /// null, the first active bank account is linked automatically.
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

    /// <summary>Masked to the last 4 digits — the full number never leaves the API.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    public string AccountHolderName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public string BankShortName { get; set; } = string.Empty;

    public string BankCode { get; set; } = string.Empty;

    public string? BankIconUrl { get; set; }

    /// <summary>True when this account is already linked to one of the customer's wallets.</summary>
    public bool AlreadyLinked { get; set; }
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

/// <summary>Aggregate result of syncing every SePay-linked wallet the customer owns.</summary>
public sealed class SepaySyncAllResponse
{
    public IReadOnlyList<SepayWalletSyncResponse> Wallets { get; set; } = Array.Empty<SepayWalletSyncResponse>();

    public int TransactionsCreated { get; set; }

    public int TransactionsUpdated { get; set; }

    /// <summary>Wallets whose sync failed — the rest still committed. Keyed by wallet id.</summary>
    public IReadOnlyDictionary<Guid, string> Failures { get; set; } = new Dictionary<Guid, string>();
}

/// <summary>Request body for listing SePay bank accounts (uses the OAuth code).</summary>
public sealed class SepayBankAccountsRequest
{
    /// <summary>
    /// OAuth2 authorization code. SePay only accepts it once, so the exchanged token is cached
    /// server-side for a few minutes and reused by the subsequent link call with the same code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The <c>state</c> issued alongside the authorize URL. Validated when present.</summary>
    public string? State { get; set; }
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

/// <summary>Connection state of one SePay-linked wallet.</summary>
public sealed class SepayLinkStatusResponse
{
    public Guid WalletId { get; set; }

    public string WalletName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    /// <summary>"oauth" or "static".</summary>
    public string AuthMode { get; set; } = string.Empty;

    public int? SepayBankAccountId { get; set; }

    public string? BankShortName { get; set; }

    /// <summary>Masked to the last 4 digits.</summary>
    public string? AccountMask { get; set; }

    public string? AccountHolderName { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>True when the stored authorization can no longer be renewed — re-link required.</summary>
    public bool RelinkRequired { get; set; }
}

/// <summary>Result of unlinking a SePay wallet.</summary>
public sealed class SepayUnlinkResponse
{
    public Guid WalletId { get; set; }

    /// <summary>The wallet type after unlinking — always <c>basic</c>.</summary>
    public string WalletType { get; set; } = "basic";

    /// <summary>Synced transactions kept on the wallet; unlinking never deletes history.</summary>
    public int TransactionsRetained { get; set; }
}

/// <summary>
/// SePay webhook payload (my.sepay.vn → Webhooks). Delivered the moment the bank posts a
/// transaction, so balances and history stay current without waiting for a manual sync.
/// </summary>
public sealed class SepayWebhookRequest
{
    /// <summary>SePay transaction id — the deduplication key, shared with the sync path.</summary>
    public long Id { get; set; }

    public string? Gateway { get; set; }

    /// <summary>VN-local timestamp, e.g. "2026-07-26 19:59:48".</summary>
    public string? TransactionDate { get; set; }

    public string? AccountNumber { get; set; }

    public string? Code { get; set; }

    public string? Content { get; set; }

    /// <summary>"in" (money received) or "out" (money sent).</summary>
    public string? TransferType { get; set; }

    public decimal TransferAmount { get; set; }

    /// <summary>Running account balance after this transaction.</summary>
    public decimal Accumulated { get; set; }

    public string? SubAccount { get; set; }

    public string? ReferenceCode { get; set; }

    public string? Description { get; set; }
}

/// <summary>Acknowledgement returned to SePay's webhook caller.</summary>
public sealed class SepayWebhookResult
{
    public bool Success { get; set; } = true;

    /// <summary>"created", "updated", or "ignored" (no wallet matched the account number).</summary>
    public string Outcome { get; set; } = string.Empty;

    public Guid? WalletId { get; set; }
}
