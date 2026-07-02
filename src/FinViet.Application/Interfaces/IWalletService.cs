using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;

namespace FinViet.Application.Interfaces;

public interface IWalletService
{
    Task<WalletListResponse> GetWalletsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<WalletResponse?> GetWalletByIdAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);

    Task<WalletResponse> CreateWalletAsync(
        Guid customerId,
        CreateWalletRequest request,
        CancellationToken cancellationToken = default);

    Task<WalletResponse?> UpdateWalletAsync(
        Guid customerId,
        Guid walletId,
        UpdateWalletRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);

    Task<TransferWalletResponse> TransferAsync(
        Guid customerId,
        TransferWalletRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<WithdrawWalletResponse> WithdrawAsync(
        Guid customerId,
        WithdrawWalletRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PagedResult<WalletTransactionResponse>> GetWalletTransactionsAsync(
      Guid customerId,
      Guid walletId,
      WalletTransactionQuery query,
      CancellationToken cancellationToken = default);
}
