namespace FinViet.Infrastructure.ExternalServices.Finverse;

/// <summary>
/// Typed wrapper over the Finverse Data API. Handles the consumer aggregation flow:
/// customer token (client credentials) → link token (hosted UI) → exchange code →
/// per-login-identity accounts/transactions.
/// </summary>
public interface IFinverseClient
{
    /// <summary>POST /auth/customer/token — backend client-credentials token (cached ~60m).</summary>
    Task<FinverseTokenResponse> GetCustomerTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /link/token — returns the hosted Link UI url to open for the user.</summary>
    Task<FinverseTokenResponse> CreateLinkAsync(
        string customerToken, string userId, string state, CancellationToken cancellationToken = default);

    /// <summary>POST /auth/token — exchange the redirect <c>code</c> for login-identity tokens.</summary>
    Task<FinverseExchangeResponse> ExchangeCodeAsync(
        string customerToken, string code, CancellationToken cancellationToken = default);

    /// <summary>POST /auth/token/refresh — refresh an expired login-identity token.</summary>
    Task<FinverseExchangeResponse> RefreshAsync(
        string customerToken, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>GET /accounts — accounts (with balances) for a login identity.</summary>
    Task<FinverseAccountsResponse> GetAccountsAsync(
        string loginIdentityToken, CancellationToken cancellationToken = default);

    /// <summary>GET /transactions/{account_id} — transactions for one account.</summary>
    Task<FinverseTransactionsResponse> GetTransactionsAsync(
        string loginIdentityToken, string accountId, int offset = 0, int limit = 500,
        CancellationToken cancellationToken = default);
}
