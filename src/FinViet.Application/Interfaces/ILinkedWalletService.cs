using FinViet.Application.DTOs.LinkedWallets;

namespace FinViet.Application.Interfaces;

/// <summary>
/// Linked-wallet (SePay) flow. Adapts a Plaid-style connect/exchange/sync surface over SePay's
/// static Bearer token: institutions is a static catalog, connect-token/exchange is a simulated
/// handshake (the raw SePay token never reaches the client), and sync pulls SePay transactions
/// into a wallet via since_id polling.
/// </summary>
public interface ILinkedWalletService
{
    IReadOnlyList<InstitutionResponse> GetInstitutions(string? country);

    /// <summary>
    /// Validates the customer's SePay token against SePay and, on success, returns an opaque
    /// access-token handle plus the bank accounts visible under that token.
    /// </summary>
    Task<ConnectResponse> ConnectAsync(
        ConnectRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinkedAccountResponse>> GetAccountsAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds the SePay token (resolved from the access-token handle) to a sepay_linked wallet,
    /// storing it encrypted on wallet_links so accounts/sync can use it per-user.
    /// </summary>
    Task<LinkWalletResponse> LinkAsync(
        Guid customerId,
        Guid walletId,
        LinkWalletRequest request,
        CancellationToken cancellationToken = default);

    Task<SyncResultResponse> SyncAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);
}
