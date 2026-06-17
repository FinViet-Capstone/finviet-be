using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface ITransactionRepository
{
    Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> CreateAsync(Guid walletId, Guid? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto> UpdateAsync(Guid transactionId, Guid? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponseDto?> ClassifyAsync(Guid transactionId, Guid? categoryId, Guid? sourceId, CancellationToken cancellationToken = default);
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
