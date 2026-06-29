namespace FinViet.Infrastructure.ExternalServices.Finverse;

// DTOs mirroring the Finverse Data API. JSON is snake_case → mapped via
// JsonNamingPolicy.SnakeCaseLower configured on the client's JsonSerializerOptions.

/// <summary>Money value used across accounts/transactions: { currency, value }.</summary>
public class FinverseMoney
{
    public string Currency { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

/// <summary>Response of /auth/customer/token and /link/token (shared shape; link adds link_url).</summary>
public class FinverseTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; }
    /// <summary>Only present on /link/token — the hosted Link UI URL.</summary>
    public string? LinkUrl { get; set; }
}

/// <summary>Response of /auth/token (exchange code) and /auth/token/refresh.</summary>
public class FinverseExchangeResponse
{
    public string AccessToken { get; set; } = string.Empty;       // login_identity_token
    public string? RefreshToken { get; set; }                     // login_identity_refresh_token
    public string LoginIdentityId { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; }
}

public class FinverseAccountsResponse
{
    public List<FinverseAccount> Accounts { get; set; } = new();
}

public class FinverseAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public string? AccountCurrency { get; set; }
    public string? AccountNumberMasked { get; set; }
    public FinverseMoney? Balance { get; set; }
}

public class FinverseTransactionsResponse
{
    public List<FinverseTransaction> Transactions { get; set; } = new();
    public int TotalTransactions { get; set; }
}

public class FinverseTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public FinverseMoney? Amount { get; set; }
    public string? Description { get; set; }
    public string? TransactionDate { get; set; }   // "YYYY-MM-DD"
    public string? PostedDate { get; set; }
    public bool IsPending { get; set; }
}
