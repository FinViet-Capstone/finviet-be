using FinViet.Application.DTOs.LinkedWallets;
using FinViet.Application.DTOs.Wallets;

namespace FinViet.Application.Interfaces;

/// <summary>
/// Finverse consumer bank-aggregation flow. Create a hosted Link session, then exchange the
/// redirect code for login-identity tokens, create a wallet per linked account, and import
/// the account's transactions.
/// </summary>
public interface IFinverseLinkService
{
    Task<FinverseLinkResponse> CreateLinkAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes linking: exchanges the code, creates a finverse_linked wallet for each account
    /// discovered, imports transactions, and returns the created wallets.
    /// </summary>
    Task<IReadOnlyList<WalletResponse>> ExchangeAsync(
        Guid customerId, FinverseExchangeRequest request, CancellationToken cancellationToken = default);
}
