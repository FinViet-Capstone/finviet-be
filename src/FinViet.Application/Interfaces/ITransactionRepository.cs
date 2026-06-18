using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface ITransactionRepository
{
    Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> CreateAsync(Guid customerId, Guid walletId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string? description, string? merchant, string? entryMethod, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> UpdateAsync(Guid customerId, Guid transactionId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string? description, string? merchant, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid customerId, Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto?> ClassifyAsync(Guid customerId, Guid transactionId, string? categoryId, CancellationToken cancellationToken = default);
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
    public string WalletName { get; set; } = string.Empty;
    public string WalletType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
