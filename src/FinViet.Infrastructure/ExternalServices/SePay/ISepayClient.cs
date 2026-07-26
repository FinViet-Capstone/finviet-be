namespace FinViet.Infrastructure.ExternalServices.SePay;

internal interface ISepayClient
{
    /// <summary>Exchange an OAuth2 authorization code for access + refresh tokens.</summary>
    Task<SepayTokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Refresh an access token using a refresh token.</summary>
    Task<SepayTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Get the authenticated SePay user profile.</summary>
    Task<SepayUser> GetMeAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>List all bank accounts connected to the SePay user.</summary>
    Task<List<SepayBankAccount>> GetBankAccountsAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Get a single bank account by id.</summary>
    Task<SepayBankAccount> GetBankAccountAsync(string accessToken, int bankAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch transactions for a bank account. Supports pagination and date filtering.
    /// </summary>
    Task<SepayTransactionListResponse> GetTransactionsAsync(
        string accessToken,
        int bankAccountId,
        int page = 1,
        int limit = 100,
        string? fromDate = null,
        string? toDate = null,
        CancellationToken cancellationToken = default);

    // ── Webhook management (OAuth scopes webhook:read / :write / :delete) ───

    /// <summary>List the webhooks registered on the authenticated SePay account.</summary>
    Task<List<SepayWebhookInfo>> GetWebhooksAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>Register a webhook and return the id SePay assigned to it.</summary>
    Task<int> CreateWebhookAsync(
        string accessToken,
        SepayWebhookCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a webhook by id.</summary>
    Task DeleteWebhookAsync(
        string accessToken,
        int webhookId,
        CancellationToken cancellationToken = default);

    // ── Static User API (personal token) ────────────────────────────────────

    /// <summary>
    /// List transactions using a personal SePay User API token (my.sepay.vn/userapi).
    /// No OAuth — the token is a single long-lived credential tied to the user's account.
    /// </summary>
    Task<SepayUserApiListResponse> GetUserApiTransactionsAsync(
        string apiToken,
        int limit = 5000,
        string? accountNumber = null,
        string? sinceDate = null,
        CancellationToken cancellationToken = default);
}
