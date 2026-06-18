using System.Data;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private const string SavingsGoalCategoryId = "cat_savings_goal";
    private const string UncategorizedName = "Chưa phân loại";

    private static readonly HashSet<string> AllowedEntryMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "manual", "photo", "sms_paste", "csv_import", "sepay_sync"
    };

    private readonly FinVietDbContext _context;

    public TransactionRepository(FinVietDbContext context)
    {
        _context = context;
    }

    public async Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        return transaction is null ? null! : ToDto(transaction);
    }

    public async Task<TransactionResponseDto> CreateAsync(
        Guid customerId,
        Guid walletId,
        string? categoryId,
        string transactionType,
        decimal amount,
        DateTime transactionDate,
        string? description,
        string? merchant,
        string? entryMethod,
        CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var wallet = await LockActiveWalletAsync(customerId, walletId, cancellationToken);
        if (wallet is null)
            throw new NotFoundException("Wallet", walletId);

        await ValidateCategoryAsync(customerId, categoryId, transactionType, cancellationToken);

        var currentBalance = GetRequiredBalance(wallet);
        var newBalance = ApplyEffect(currentBalance, transactionType, amount);
        EnsureBalanceAllowed(wallet, currentBalance, newBalance);

        var now = DateTime.UtcNow;
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            WalletId = walletId,
            CustomerId = customerId,
            CategoryId = categoryId,
            TransactionType = transactionType,
            EntryMethod = NormalizeEntryMethod(entryMethod),
            Amount = amount,
            TransactionDate = transactionDate,
            Description = description,
            Merchant = merchant,
            CreatedAt = now,
            UpdatedAt = now
        };

        wallet.Balance = newBalance;
        _context.Transactions.Add(transaction);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return ToDto(transaction);
    }

    public async Task<TransactionResponseDto> UpdateAsync(
        Guid customerId,
        Guid transactionId,
        string? categoryId,
        string transactionType,
        decimal amount,
        DateTime transactionDate,
        string? description,
        string? merchant,
        CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);

        if (transaction is null)
            return null!;

        var wallet = await LockActiveWalletAsync(customerId, transaction.WalletId, cancellationToken);
        if (wallet is null)
            throw new ForbiddenException("You do not have access to this transaction.");

        await ValidateCategoryAsync(customerId, categoryId, transactionType, cancellationToken);

        var currentBalance = GetRequiredBalance(wallet);
        var balanceWithoutOldTransaction = ReverseEffect(currentBalance, transaction.TransactionType, transaction.Amount);
        var newBalance = ApplyEffect(balanceWithoutOldTransaction, transactionType, amount);
        EnsureBalanceAllowed(wallet, currentBalance, newBalance);

        transaction.CustomerId = customerId;
        transaction.CategoryId = categoryId;
        transaction.TransactionType = transactionType;
        transaction.Amount = amount;
        transaction.TransactionDate = transactionDate;
        transaction.Description = description;
        transaction.Merchant = merchant;
        transaction.UpdatedAt = DateTime.UtcNow;
        wallet.Balance = newBalance;

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return ToDto(transaction);
    }

    public async Task<bool> DeleteAsync(Guid customerId, Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == id, cancellationToken);

        if (transaction is null)
            return false;

        if (transaction.TransferPairId.HasValue)
            return await DeleteTransferPairAsync(transaction.TransferPairId.Value, customerId, cancellationToken);

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        transaction = await _context.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == id, cancellationToken);

        if (transaction is null)
            return false;

        var wallet = await LockActiveWalletAsync(customerId, transaction.WalletId, cancellationToken);
        if (wallet is null)
            throw new ForbiddenException("You do not have access to this transaction.");

        wallet.Balance = ReverseEffect(GetRequiredBalance(wallet), transaction.TransactionType, transaction.Amount);
        _context.Transactions.Remove(transaction);

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<TransactionResponseDto?> ClassifyAsync(
        Guid customerId,
        Guid transactionId,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions
            .Include(t => t.Wallet)
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);

        if (transaction is null)
            return null;

        if (transaction.Wallet is null ||
            transaction.Wallet.CustomerId != customerId ||
            transaction.Wallet.IsDeleted)
        {
            throw new ForbiddenException("You do not have access to this transaction.");
        }

        await ValidateCategoryAsync(customerId, categoryId, transaction.TransactionType, cancellationToken);

        transaction.CustomerId = customerId;
        transaction.CategoryId = categoryId;
        transaction.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDto(transaction);
    }

    private async Task<bool> DeleteTransferPairAsync(
        Guid transferPairId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var legs = await _context.Transactions
            .Where(t => t.TransferPairId == transferPairId)
            .ToListAsync(cancellationToken);

        if (legs.Count == 0)
            return false;

        var walletIds = legs
            .Select(t => t.WalletId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var wallets = new List<Wallet>(walletIds.Length);
        foreach (var walletId in walletIds)
        {
            var wallet = await LockActiveWalletAsync(customerId, walletId, cancellationToken);
            if (wallet is null)
                throw new ForbiddenException("You do not have access to this transfer.");

            wallets.Add(wallet);
        }

        foreach (var leg in legs)
        {
            var wallet = wallets.First(w => w.WalletId == leg.WalletId);
            wallet.Balance = ReverseEffect(GetRequiredBalance(wallet), leg.TransactionType, leg.Amount);
        }

        _context.Transactions.RemoveRange(legs);
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Wallet?> LockActiveWalletAsync(
        Guid customerId,
        Guid walletId,
        CancellationToken cancellationToken)
    {
        return await _context.Wallets
            .FromSqlInterpolated($"""
                SELECT wallet_id, customer_id, wallet_name, wallet_type, balance, is_deleted, deleted_at
                FROM wallet
                WHERE customer_id = {customerId}
                  AND wallet_id = {walletId}
                  AND is_deleted = false
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task ValidateCategoryAsync(
        Guid customerId,
        string? categoryId,
        string transactionType,
        CancellationToken cancellationToken)
    {
        if (transactionType is "transfer_in" or "transfer_out")
        {
            if (!string.IsNullOrWhiteSpace(categoryId))
                throw new BadRequestException("Transfer transactions cannot be assigned to a category.");

            return;
        }

        if (string.IsNullOrWhiteSpace(categoryId))
            return;

        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            throw new NotFoundException("Category", categoryId);

        if (!category.Type.Equals(transactionType, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException(
                $"Category '{categoryId}' is for '{category.Type}' transactions, not '{transactionType}'.");
        }

        if (!transactionType.Equals("expense", StringComparison.OrdinalIgnoreCase))
            return;

        if (category.CategoryId == SavingsGoalCategoryId ||
            category.CategoryName.Equals(UncategorizedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("This category is system-managed and cannot be selected manually.");
        }

        var hasCategorySet = await _context.CustomerCategories
            .AnyAsync(x => x.CustomerId == customerId, cancellationToken);

        if (!hasCategorySet)
            await SeedCustomerCategoriesAsync(customerId, cancellationToken);

        var isActiveForCustomer = await _context.CustomerCategories
            .AnyAsync(
                x => x.CustomerId == customerId &&
                     x.CategoryId == categoryId &&
                     x.IsActive,
                cancellationToken);

        if (!isActiveForCustomer)
            throw new ForbiddenException("Category is not available for this customer.");
    }

    private async Task SeedCustomerCategoriesAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customerExists = await _context.Customers
            .AnyAsync(c => c.CustomerId == customerId, cancellationToken);

        if (!customerExists)
            throw new NotFoundException("Customer", customerId);

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c =>
                c.Type == "expense" &&
                c.CategoryId != SavingsGoalCategoryId &&
                c.CategoryName != UncategorizedName)
            .Select(c => new
            {
                c.CategoryId,
                c.ExpenseClass
            })
            .ToListAsync(cancellationToken);

        foreach (var category in categories)
        {
            _context.CustomerCategories.Add(new CustomerCategory
            {
                CustomerId = customerId,
                CategoryId = category.CategoryId,
                BucketId = ToBudgetBucketId(category.ExpenseClass),
                Source = "system",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string ToBudgetBucketId(string? expenseClass)
    {
        if (string.IsNullOrWhiteSpace(expenseClass))
            return "needs";

        return expenseClass.Trim().ToUpperInvariant() switch
        {
            "WANT" or "WANTS" => "wants",
            "SAVING" or "SAVINGS" => "savings",
            _ => "needs"
        };
    }

    private static string NormalizeEntryMethod(string? entryMethod)
    {
        if (string.IsNullOrWhiteSpace(entryMethod))
            return "manual";

        var normalized = entryMethod.Trim().ToLowerInvariant();
        if (!AllowedEntryMethods.Contains(normalized))
        {
            throw new BadRequestException(
                "Entry method must be one of: manual, photo, sms_paste, csv_import, sepay_sync.");
        }

        return normalized;
    }

    private static decimal ApplyEffect(decimal currentBalance, string transactionType, decimal amount)
        => IsCredit(transactionType) ? currentBalance + amount : currentBalance - amount;

    private static decimal ReverseEffect(decimal currentBalance, string transactionType, decimal amount)
        => IsCredit(transactionType) ? currentBalance - amount : currentBalance + amount;

    private static bool IsCredit(string transactionType)
        => transactionType is "income" or "transfer_in";

    private static void EnsureBalanceAllowed(Wallet wallet, decimal previousBalance, decimal newBalance)
    {
        if (newBalance < 0 && newBalance < previousBalance)
        {
            throw new BusinessRuleException(
                "Wallet balance is insufficient for this transaction.",
                "insufficient_balance");
        }
    }

    private static decimal GetRequiredBalance(Wallet wallet)
        => wallet.Balance
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing balance.");

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
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.WalletId == id && !w.IsDeleted, cancellationToken);

        if (wallet == null)
            return null!;

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
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.WalletId == id && !w.IsDeleted, cancellationToken);

        if (wallet == null)
            return null!;

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
