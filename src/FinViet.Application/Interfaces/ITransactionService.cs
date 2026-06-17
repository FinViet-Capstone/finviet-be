using FinViet.Application.Common;
using FinViet.Application.DTOs.Transactions;

namespace FinViet.Application.Interfaces;

/// <summary>Transaction APIs (spec §4). All operations are scoped to the authenticated customer.</summary>
public interface ITransactionService
{
    Task<PagedResult<TransactionResponse>> GetTransactionsAsync(
        Guid customerId, TransactionQuery query, CancellationToken cancellationToken = default);

    Task<TransactionResponse?> GetByIdAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken = default);

    Task<TransactionResponse> CreateAsync(
        Guid customerId, CreateTransactionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionResponse>> CreateBatchAsync(
        Guid customerId, BatchTransactionRequest request, CancellationToken cancellationToken = default);

    Task<TransactionResponse?> UpdateAsync(
        Guid customerId, Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken = default);

    Task<TransferResponse> TransferAsync(
        Guid customerId, TransferRequest request, CancellationToken cancellationToken = default);

    Task<TransactionSummaryResponse> GetSummaryAsync(
        Guid customerId, int year, int month, CancellationToken cancellationToken = default);
}
