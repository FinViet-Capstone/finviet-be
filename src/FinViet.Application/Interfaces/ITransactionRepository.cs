using FinViet.Application.Common;
using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface ITransactionRepository
{
    Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // ── Read APIs (customer-scoped via wallet ownership) ────────────────────────
    Task<PagedResult<TransactionResponseDto>> GetPagedAsync(Guid customerId, TransactionQueryDto filter, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto?> GetByIdForCustomerAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default);
    Task<TransactionSummaryResponseDto> GetSummaryAsync(Guid customerId, int year, int month, CancellationToken cancellationToken = default);

    Task<TransactionResponseDto> CreateAsync(Guid walletId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> CreateManualForCustomerAsync(Guid customerId, Guid walletId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string? note, string? idempotencyKey, string? entryMethod = null, string? merchant = null, string? aiSource = null, decimal? aiConfidence = null, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> UpdateAsync(Guid transactionId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteForCustomerAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Splits one transaction across categories, replacing it with sibling rows that share a
    /// split group id. The parts must sum to the original amount, so the wallet balance is
    /// unchanged. Throws <c>BusinessRuleException</c> for a synced transaction, a transfer
    /// leg, a saving-goal transaction, fewer than two parts, a non-positive part, or a total
    /// that does not match.
    /// </summary>
    Task<IReadOnlyList<TransactionResponseDto>> SplitForCustomerAsync(Guid customerId, Guid transactionId, IReadOnlyList<SplitPartRequest> parts, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto?> ClassifyAsync(Guid transactionId, string? categoryId, CancellationToken cancellationToken = default);

    // Partial update: a null parameter leaves that field unchanged. Reverses/reapplies the
    // wallet balance delta under lock when amount changes. Returns null if not found/not owned.
    Task<TransactionResponseDto?> EditForCustomerAsync(
        Guid customerId,
        Guid transactionId,
        string? categoryId,
        decimal? amount,
        string? merchant,
        DateTime? transactionDate,
        CancellationToken cancellationToken = default);
}

public interface IWalletRepository
{
    Task<WalletDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WalletDto> UpdateBalanceAsync(Guid id, decimal newBalance, CancellationToken cancellationToken = default);
}

public class WalletDto
{
    public Guid WalletId { get; set; }
    public Guid CustomerId { get; set; }
    public string WalletName { get; set; }
    public string WalletType { get; set; }
    public decimal Balance { get; set; }
}
