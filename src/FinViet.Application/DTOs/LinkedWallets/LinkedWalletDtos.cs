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

/// <summary>Result of linking a wallet to a SePay token.</summary>
public class LinkWalletResponse
{
    public Guid WalletId { get; set; }
    public string? SepayAccountId { get; set; }
    public string? BankName { get; set; }
    public string? AccountMask { get; set; }
}

/// <summary>Outcome of pulling SePay transactions into a wallet.</summary>
public class SyncResultResponse
{
    public Guid WalletId { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
