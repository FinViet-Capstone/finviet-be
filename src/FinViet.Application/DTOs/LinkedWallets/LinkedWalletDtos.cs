namespace FinViet.Application.DTOs.LinkedWallets;

/// <summary>A bank the user can link via SePay. Static catalog (SePay has no institutions API).</summary>
public class InstitutionResponse
{
    public string Id { get; set; } = string.Empty;       // bank code, e.g. "BIDV"
    public string Name { get; set; } = string.Empty;     // full name
    public string BankCode { get; set; } = string.Empty;
    public string Country { get; set; } = "VN";
    public string? Logo { get; set; }
}

/// <summary>A bank account discovered through the linked SePay token.</summary>
public class LinkedAccountResponse
{
    public string SepayAccountId { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public string? HolderName { get; set; }
    public string? BankCode { get; set; }
    public string? BankName { get; set; }
}

/// <summary>Body for connecting a SePay account: the customer's own SePay API token.</summary>
public class ConnectRequest
{
    public string SepayToken { get; set; } = string.Empty;
}

/// <summary>
/// Result of a successful connect: an opaque access-token handle plus the bank accounts visible
/// under that token (saves a follow-up /accounts call).
/// </summary>
public class ConnectResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public IReadOnlyList<LinkedAccountResponse> Accounts { get; set; } = new List<LinkedAccountResponse>();
}

/// <summary>Body for linking a wallet to the SePay token resolved by <c>accessToken</c>.</summary>
public class LinkWalletRequest
{
    public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
/// Body for the one-step link flow: creates a new sepay_linked wallet for the chosen account and
/// binds the SePay token to it. <see cref="SepayAccountId"/> is optional — defaults to the first
/// account discovered under the token.
/// </summary>
public class LinkAccountRequest
{
    public string AccessToken { get; set; } = string.Empty;
    public string? SepayAccountId { get; set; }
}

/// <summary>Result of linking a wallet to a SePay token.</summary>
public class LinkWalletResponse
{
    public Guid WalletId { get; set; }
    public string? SepayAccountId { get; set; }
    public string? BankName { get; set; }
    public string? AccountMask { get; set; }
}

// ── Finverse (consumer bank aggregation) ──────────────────────────────────────

/// <summary>Result of starting a Finverse link session: the hosted Link UI url to open.</summary>
public class FinverseLinkResponse
{
    public string LinkUrl { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    /// <summary>The redirect URI the client's WebView should watch for to capture the auth code.</summary>
    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>Body for completing a Finverse link: the <c>code</c> captured from the redirect.</summary>
public class FinverseExchangeRequest
{
    public string Code { get; set; } = string.Empty;
    public string? State { get; set; }
}

/// <summary>Outcome of pulling SePay transactions into a wallet.</summary>
public class SyncResultResponse
{
    public Guid WalletId { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
