using FinViet.Application.DTOs.Wallets;

namespace FinViet.Application.Interfaces;

public interface ISepayWalletService
{
    /// <summary>
    /// Build the SePay OAuth2 authorization URL together with a signed, expiring <c>state</c>
    /// that binds the flow to this customer.
    /// </summary>
    SepayAuthorizeUrlResponse CreateAuthorizeUrl(Guid customerId);

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
    /// List the SePay user's bank accounts for an authorization code, so the client can let the
    /// user pick one before linking. The exchanged token is cached, so the same code still works
    /// for the follow-up link call.
    /// </summary>
    Task<IReadOnlyList<SepayBankAccountResponse>> GetBankAccountsAsync(
        Guid customerId,
        SepayBankAccountsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Connection state of every SePay-linked wallet the customer owns.</summary>
    Task<IReadOnlyList<SepayLinkStatusResponse>> GetLinksAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync transactions for an existing sepay_linked wallet. Refreshes the token if expired,
    /// fetches new transactions from SePay, and upserts them. AI categorization runs on new expenses.
    /// </summary>
    Task<SepayWalletSyncResponse> SyncWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync every SePay-linked wallet the customer owns. A failure on one wallet is reported and
    /// does not abort the others.
    /// </summary>
    Task<SepaySyncAllResponse> SyncAllWalletsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drop the SePay authorization for a wallet and turn it back into a manual (basic) wallet.
    /// Synced transactions are kept.
    /// </summary>
    Task<SepayUnlinkResponse> UnlinkWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingest one SePay webhook delivery: match the account number to a linked wallet, upsert the
    /// transaction, and refresh the wallet balance. Unmatched deliveries are acknowledged and
    /// ignored so SePay does not retry forever.
    /// </summary>
    Task<SepayWebhookResult> HandleWebhookAsync(
        string? apiKeyHeader,
        SepayWebhookRequest payload,
        CancellationToken cancellationToken = default);
}
