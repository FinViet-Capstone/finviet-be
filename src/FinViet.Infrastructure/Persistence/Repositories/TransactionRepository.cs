using System.Data;
using FinViet.Application.Interfaces;
using FinViet.Application.DTOs;
using FinViet.Application.Common.Exceptions;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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
            WalletId = transaction.WalletId,
            CategoryId = transaction.CategoryId,
            SourceId = transaction.SourceId,
            TransactionType = transaction.TransactionType,
            SourceChannel = transaction.SourceChannel,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.Now,
            Note = transaction.Note,
            CreatedAt = transaction.TransactionDate ?? DateTime.Now,
            TransferPairId = transaction.TransferPairId
        };
    }

    public async Task<bool> DeleteTransferPairAsync(Guid transferPairId, Guid customerId, CancellationToken cancellationToken = default)
    {
        await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var legs = await _context.Transactions
            .Where(t => t.TransferPairId == transferPairId)
            .ToListAsync(cancellationToken);

        if (legs.Count == 0)
            return false;

        // Khóa các ví liên quan (kể cả ví đã soft-delete) theo thứ tự cố định để chống deadlock.
        var walletIds = legs.Select(l => l.WalletId).Distinct().OrderBy(x => x).ToList();
        var wallets = await _context.Wallets
            .FromSqlInterpolated($@"
                SELECT wallet_id, customer_id, wallet_name, wallet_type, balance, is_deleted, deleted_at
                FROM wallet
                WHERE wallet_id = ANY({walletIds})
                ORDER BY wallet_id
                FOR UPDATE")
            .ToListAsync(cancellationToken);

        // Ownership: mọi ví của cặp transfer phải thuộc customer hiện tại.
        if (wallets.Any(w => w.CustomerId != customerId))
            throw new ForbiddenException("You do not have access to this transfer.");

        foreach (var leg in legs)
        {
            var wallet = wallets.FirstOrDefault(w => w.WalletId == leg.WalletId);
            if (wallet is not null && wallet.Balance.HasValue)
            {
                if (leg.TransactionType == "TRANSFER_OUT")
                    wallet.Balance = wallet.Balance.Value + leg.Amount;   // hoàn lại ví nguồn
                else if (leg.TransactionType == "TRANSFER_IN")
                    wallet.Balance = wallet.Balance.Value - leg.Amount;   // rút khỏi ví đích
            }

            _context.Transactions.Remove(leg);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);
        return true;
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
            SourceChannel = "MANUAL",
            Amount = amount,
            TransactionDate = transactionDate,
            Note = note
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return new TransactionResponseDto
        {
            TransactionId = transaction.TransactionId,
            WalletId = transaction.WalletId,
            CategoryId = transaction.CategoryId,
            SourceId = transaction.SourceId,
            TransactionType = transaction.TransactionType,
            SourceChannel = transaction.SourceChannel,
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
            WalletId = transaction.WalletId,
            CategoryId = transaction.CategoryId,
            SourceId = transaction.SourceId,
            TransactionType = transaction.TransactionType,
            SourceChannel = transaction.SourceChannel,
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

    public async Task<TransactionResponseDto?> ClassifyAsync(Guid transactionId, Guid? categoryId, Guid? sourceId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { transactionId }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null;

        transaction.CategoryId = categoryId;
        transaction.SourceId = sourceId;

        await _context.SaveChangesAsync(cancellationToken);

        return new TransactionResponseDto
        {
            TransactionId = transaction.TransactionId,
            WalletId = transaction.WalletId,
            CategoryId = transaction.CategoryId,
            SourceId = transaction.SourceId,
            TransactionType = transaction.TransactionType,
            SourceChannel = transaction.SourceChannel,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate ?? DateTime.Now,
            Note = transaction.Note,
            CreatedAt = transaction.TransactionDate ?? DateTime.Now
        };
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
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.WalletId == id && !w.IsDeleted, cancellationToken);
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

