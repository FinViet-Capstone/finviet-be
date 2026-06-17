using FinViet.Application.Interfaces;
using FinViet.Application.DTOs;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly FinVietDbContext _context;

    public TransactionRepository(FinVietDbContext context)
    {
        _context = context;
    }

    public async Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null;

        return ToDto(transaction);
    }

    public async Task<TransactionResponseDto> CreateAsync(Guid walletId, Guid? customerId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string? description, string? merchant, string? entryMethod, CancellationToken cancellationToken = default)
    {
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            WalletId = walletId,
            CustomerId = customerId,
            CategoryId = categoryId,
            TransactionType = transactionType,
            EntryMethod = entryMethod ?? "manual",
            Amount = amount,
            TransactionDate = transactionDate,
            Description = description,
            Merchant = merchant,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(transaction);
    }

    public async Task<TransactionResponseDto> UpdateAsync(Guid transactionId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string? description, string? merchant, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { transactionId }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null;

        transaction.CategoryId = categoryId;
        transaction.TransactionType = transactionType;
        transaction.Amount = amount;
        transaction.TransactionDate = transactionDate;
        transaction.Description = description;
        transaction.Merchant = merchant;
        transaction.UpdatedAt = DateTime.UtcNow;

        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(transaction);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (transaction == null)
            return false;

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TransactionResponseDto?> ClassifyAsync(Guid transactionId, string? categoryId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { transactionId }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null;

        transaction.CategoryId = categoryId;
        transaction.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(transaction);
    }

    private static TransactionResponseDto ToDto(Transaction transaction)
        => new()
        {
            TransactionId = transaction.TransactionId,
            CustomerId = transaction.CustomerId,
            WalletId = transaction.WalletId,
            CategoryId = transaction.CategoryId,
            TransactionType = transaction.TransactionType,
            EntryMethod = transaction.EntryMethod,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.UtcNow,
            Description = transaction.Description,
            Merchant = transaction.Merchant,
            ExternalId = transaction.ExternalId,
            TransferPairId = transaction.TransferPairId,
            CreatedAt = transaction.CreatedAt
        };
}

public class WalletRepository : IWalletRepository
{
    private readonly FinVietDbContext _context;

    public WalletRepository(FinVietDbContext context)
    {
        _context = context;
    }

    public async Task<WalletDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (wallet == null)
            return null;

        return new WalletDto
        {
            WalletId = wallet.WalletId,
            CustomerId = GetRequiredCustomerId(wallet),
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType,
            Balance = GetRequiredBalance(wallet)
        };
    }

    public async Task<WalletDto> UpdateBalanceAsync(Guid id, decimal newBalance, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (wallet == null)
            return null;

        wallet.Balance = newBalance;
        _context.Wallets.Update(wallet);
        await _context.SaveChangesAsync(cancellationToken);

        return new WalletDto
        {
            WalletId = wallet.WalletId,
            CustomerId = GetRequiredCustomerId(wallet),
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType,
            Balance = GetRequiredBalance(wallet)
        };
    }

    private static Guid GetRequiredCustomerId(Wallet wallet)
        => wallet.CustomerId
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing customer_id.");

    private static decimal GetRequiredBalance(Wallet wallet)
        => wallet.Balance
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing balance.");
}

