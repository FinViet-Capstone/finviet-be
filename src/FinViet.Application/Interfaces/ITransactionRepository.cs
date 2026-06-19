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

    Task<TransactionResponseDto> CreateAsync(Guid walletId, string? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> UpdateAsync(Guid transactionId, string? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto?> ClassifyAsync(Guid transactionId, string? categoryId, Guid? sourceId, CancellationToken cancellationToken = default);
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
