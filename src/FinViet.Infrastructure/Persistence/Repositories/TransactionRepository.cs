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

        return new TransactionResponseDto
        {
            TransactionId = transaction.TransactionId,
            WalletId = transaction.WalletId ?? Guid.Empty,
            CategoryId = transaction.CategoryId,
            TransactionType = transaction.TransactionType,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.Now,
            Note = transaction.Note,
            CreatedAt = transaction.TransactionDate ?? DateTime.Now
        };
    }

    public async Task<TransactionResponseDto> CreateAsync(Guid walletId, Guid? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default)
    {
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            WalletId = walletId,
            CategoryId = categoryId,
            SourceId = sourceId,
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDate,
            Note = note
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return new TransactionResponseDto
        {
            TransactionId = transaction.TransactionId,
            WalletId = transaction.WalletId ?? Guid.Empty,
            CategoryId = transaction.CategoryId,
            TransactionType = transaction.TransactionType,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.Now,
            Note = transaction.Note,
            CreatedAt = transaction.TransactionDate ?? DateTime.Now
        };
    }

    public async Task<TransactionResponseDto> UpdateAsync(Guid transactionId, Guid? categoryId, Guid? sourceId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { transactionId }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null;

        transaction.CategoryId = categoryId;
        transaction.SourceId = sourceId;
        transaction.TransactionType = transactionType;
        transaction.Amount = amount;
        transaction.TransactionDate = transactionDate;
        transaction.Note = note;

        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return new TransactionResponseDto
        {
            TransactionId = transaction.TransactionId,
            WalletId = transaction.WalletId ?? Guid.Empty,
            CategoryId = transaction.CategoryId,
            TransactionType = transaction.TransactionType,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.Now,
            Note = transaction.Note,
            CreatedAt = transaction.TransactionDate ?? DateTime.Now
        };
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
            CustomerId = wallet.CustomerId,
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType,
            Balance = wallet.Balance
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
            CustomerId = wallet.CustomerId,
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType,
            Balance = wallet.Balance
        };
    }
}

