using FinViet.Application.DTOs.Wallets;

namespace FinViet.Application.Interfaces;

public interface IFinverseWalletService
{
    Task<FinverseLinkTokenResponse> CreateLinkTokenAsync(
        Guid customerId,
        CreateFinverseLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<FinverseLinkResult> CompleteLinkAsync(
        Guid customerId,
        CompleteFinverseLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<FinverseLinkResult> CompleteLinkCallbackAsync(
        CompleteFinverseLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<FinverseWalletSyncResponse> SyncWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken = default);
}
