namespace FinViet.Infrastructure.ExternalServices.Sepay;

/// <summary>
/// Typed wrapper over the SePay v2 REST API. Each call takes the caller's SePay API token
/// (Bearer) so the client is per-user, not bound to a single configured token.
/// </summary>
public interface ISepayClient
{
    /// <summary>
    /// Pulls transactions, oldest-first. When <paramref name="sinceId"/> is set, returns only
    /// transactions newer than that SePay id (polling cursor).
    /// </summary>
    Task<SepayListResponse<SepayTransaction>> GetTransactionsAsync(
        string apiToken,
        string? sinceId = null,
        int perPage = 100,
        string sort = "asc",
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the bank accounts linked to the given SePay token.</summary>
    Task<SepayListResponse<SepayBankAccount>> GetBankAccountsAsync(
        string apiToken,
        CancellationToken cancellationToken = default);
}
