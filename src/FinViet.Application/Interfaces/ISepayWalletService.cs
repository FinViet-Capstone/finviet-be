using FinViet.Application.DTOs.Wallets;

namespace FinViet.Application.Interfaces;

public interface ISepayWalletService
{
    /// <summary>
    /// Exchange an OAuth2 authorization code for tokens, fetch the user's bank accounts,
    /// create a sepay_linked wallet, and perform the initial transaction sync.
    /// </summary>
    Task<SepayLinkResult> LinkAccountAsync(
        Guid customerId,
        LinkSepayAccountRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Link a bank account using a personal SePay User API token. Validates the token by
    /// fetching transactions, creates a sepay_linked wallet, and imports the history.
    /// </summary>
    Task<SepayLinkResult> LinkWithTokenAsync(
        Guid customerId,
        LinkSepayTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List the SePay user's bank accounts using a previously stored access token.
    /// Used when the user wants to link a different account after the initial OAuth flow.
    /// </summary>
    Task<IReadOnlyList<SepayBankAccountResponse>> GetBankAccountsAsync(
        Guid customerId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync transactions for an existing sepay_linked wallet. Refreshes the token if expired,
    /// fetches new transactions from SePay, and upserts them. AI categorization runs on new expenses.
    /// </summary>
    Task<SepayWalletSyncResponse> SyncWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);
}
