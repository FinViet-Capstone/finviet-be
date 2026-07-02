using FinViet.Application.DTOs.Wallets;

namespace FinViet.Infrastructure.ExternalServices.Finverse;

internal interface IFinverseClient
{
    Task<FinverseLinkTokenApiResponse> CreateLinkTokenAsync(
        Guid customerId,
        string state,
        CreateFinverseLinkRequest request,
        CancellationToken cancellationToken);

    Task<FinverseTokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task<FinverseTokenResponse> RefreshLoginIdentityTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<FinverseLoginIdentityApiResponse> GetLoginIdentityAsync(
        string loginIdentityToken,
        CancellationToken cancellationToken);

    Task<FinverseAccountsApiResponse> GetAccountsAsync(
        string loginIdentityToken,
        CancellationToken cancellationToken);

    Task<FinverseTransactionsApiResponse> GetTransactionsAsync(
        string loginIdentityToken,
        string accountId,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
