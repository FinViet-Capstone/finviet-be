using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly FinVietDbContext _context;

    public TransactionRepository(FinVietDbContext context)
    {
        _context = context;
    }

    private static readonly string[] ValidTypes = { "expense", "income", "transfer_out", "transfer_in" };

    public async Task<PagedResult<TransactionResponseDto>> GetPagedAsync(
        Guid customerId, TransactionQueryDto filter, CancellationToken cancellationToken = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

        var query = _context.Transactions
            .AsNoTracking()
            .Where(t => t.CustomerId == customerId || (t.Wallet != null && t.Wallet.CustomerId == customerId));

        if (filter.WalletId.HasValue)
            query = query.Where(t => t.WalletId == filter.WalletId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            var type = filter.Type.Trim().ToLowerInvariant();
            if (!ValidTypes.Contains(type))
                throw new BadRequestException($"type must be one of: {string.Join(", ", ValidTypes)}.");
            query = query.Where(t => t.TransactionType == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryId))
            query = query.Where(t => t.CategoryId == filter.CategoryId);

        if (filter.From.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.To.Value);

        if (filter.UncategorizedOnly)
            query = query.Where(t => t.CategoryId == null && t.TransactionType != "transfer_out" && t.TransactionType != "transfer_in");

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = $"%{filter.Q.Trim()}%";
            query = query.Where(t =>
                (t.Description != null && EF.Functions.ILike(t.Description, term)) ||
                (t.Merchant != null && EF.Functions.ILike(t.Merchant, term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var entities = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.TransactionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionResponseDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = entities.Select(MapToDto).ToList()
        };
    }

    public async Task<TransactionResponseDto?> GetByIdForCustomerAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.TransactionId == transactionId
                        && (t.CustomerId == customerId || (t.Wallet != null && t.Wallet.CustomerId == customerId)))
            .FirstOrDefaultAsync(cancellationToken);

        return transaction is null ? null : MapToDto(transaction);
    }

    public async Task<TransactionSummaryResponseDto> GetSummaryAsync(
        Guid customerId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(t => (t.CustomerId == customerId || (t.Wallet != null && t.Wallet.CustomerId == customerId))
                        && t.TransactionType != "transfer_out"
                        && t.TransactionType != "transfer_in"
                        && t.TransactionDate >= start && t.TransactionDate < end)
            .Select(t => new
            {
                t.TransactionType,
                t.Amount,
                t.CategoryId,
                t.Merchant,
                t.TransactionDate
            })
            .ToListAsync(cancellationToken);

        var income = rows.Where(r => r.TransactionType == "income").Sum(r => r.Amount);
        var expense = rows.Where(r => r.TransactionType == "expense").Sum(r => r.Amount);

        var categoryNames = await _context.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, cancellationToken);

        var byCategory = rows
            .Where(r => r.TransactionType == "expense")
            .GroupBy(r => r.CategoryId)
            .Select(g => new CategorySummaryItemDto
            {
                CategoryId = g.Key,
                CategoryName = g.Key != null && categoryNames.TryGetValue(g.Key, out var name) ? name : null,
                Total = g.Sum(x => x.Amount)
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        var byDay = rows
            .Where(r => r.TransactionDate.HasValue)
            .GroupBy(r => DateOnly.FromDateTime(r.TransactionDate!.Value))
            .Select(g => new DaySummaryItemDto
            {
                Date = g.Key,
                Income = g.Where(x => x.TransactionType == "income").Sum(x => x.Amount),
                Expense = g.Where(x => x.TransactionType == "expense").Sum(x => x.Amount),
                Net = g.Where(x => x.TransactionType == "income").Sum(x => x.Amount)
                      - g.Where(x => x.TransactionType == "expense").Sum(x => x.Amount)
            })
            .OrderBy(d => d.Date)
            .ToList();

        var topBeneficiaries = rows
            .Where(r => r.TransactionType == "expense" && !string.IsNullOrWhiteSpace(r.Merchant))
            .GroupBy(r => r.Merchant!)
            .Select(g => new BeneficiarySummaryItemDto { Beneficiary = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(m => m.Total)
            .Take(10)
            .ToList();

        return new TransactionSummaryResponseDto
        {
            Income = income,
            Expense = expense,
            Net = income - expense,
            ByCategory = byCategory,
            ByDay = byDay,
            TopBeneficiaries = topBeneficiaries
        };
    }

    public async Task<TransactionResponseDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        return transaction == null ? null! : MapToDto(transaction);
    }

    public async Task<TransactionResponseDto> CreateAsync(Guid walletId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { walletId }, cancellationToken: cancellationToken);
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = wallet?.CustomerId ?? Guid.Empty,
            WalletId = walletId,
            CategoryId = categoryId,
            TransactionType = NormalizeType(transactionType),
            EntryMethod = "manual",
            Amount = amount,
            TransactionDate = transactionDate,
            Description = note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(transaction);
    }

    public async Task<TransactionResponseDto> CreateManualForCustomerAsync(
        Guid customerId,
        Guid walletId,
        string? categoryId,
        string transactionType,
        decimal amount,
        DateTime transactionDate,
        string? note,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await using var databaseTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var requestHash = IdempotencyStore.ComputeRequestHash(new
        {
            walletId,
            categoryId,
            transactionType,
            amount,
            transactionDate,
            note
        });
        var idempotency = await IdempotencyStore.ClaimAsync(
            _context,
            customerId,
            "transaction-create",
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (idempotency.IsReplay)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
            return IdempotencyStore.ReadReplay<TransactionResponseDto>(idempotency);
        }

        var wallet = (await LockWalletsAsync(new[] { walletId }, cancellationToken)).SingleOrDefault();
        if (wallet is null || wallet.CustomerId != customerId || wallet.IsDeleted)
            throw new NotFoundException("Wallet", walletId);

        var normalizedType = NormalizeType(transactionType);
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            CategoryId = categoryId,
            TransactionType = normalizedType,
            EntryMethod = "manual",
            Amount = amount,
            TransactionDate = transactionDate,
            Description = note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var newBalance = (wallet.Balance ?? 0m) + (normalizedType == "income" ? amount : -amount);
        if (newBalance < 0)
            throw new BusinessRuleException("Source wallet does not have enough balance.", "insufficient_balance");

        wallet.Balance = newBalance;
        wallet.UpdatedAt = DateTime.UtcNow;
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        var response = MapToDto(transaction);
        await IdempotencyStore.CompleteAsync(
            _context,
            customerId,
            "transaction-create",
            idempotencyKey!,
            response,
            cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<TransactionResponseDto> UpdateAsync(Guid transactionId, string? categoryId, string transactionType, decimal amount, DateTime transactionDate, string note, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.Transactions.FindAsync(new object[] { transactionId }, cancellationToken: cancellationToken);
        if (transaction == null)
            return null!;

        transaction.CategoryId = categoryId;
        transaction.TransactionType = NormalizeType(transactionType);
        transaction.Amount = amount;
        transaction.TransactionDate = transactionDate;
        transaction.Description = note;
        transaction.UpdatedAt = DateTime.UtcNow;

        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(transaction);
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

    public async Task<bool> DeleteForCustomerAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        await using var databaseTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var target = await _context.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.CustomerId == customerId, cancellationToken);
        if (target is null)
            return false;

        if (string.Equals(target.EntryMethod, "sepay_sync", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("SePay-synced transactions cannot be deleted.", "sepay_transaction_locked");

        if (target.TransferPairId.HasValue)
        {
            var pair = await _context.Transactions
                .Where(t => t.CustomerId == customerId && t.TransferPairId == target.TransferPairId)
                .ToListAsync(cancellationToken);

            if (pair.Count != 2
                || pair.Count(t => t.TransactionType == "transfer_out") != 1
                || pair.Count(t => t.TransactionType == "transfer_in") != 1)
            {
                throw new BusinessRuleException("Transfer pair is incomplete and cannot be reversed safely.", "transfer_pair_invalid");
            }

            var wallets = await LockWalletsAsync(pair.Select(t => t.WalletId).Distinct().ToArray(), cancellationToken);
            if (wallets.Count != 2 || wallets.Any(w => w.CustomerId != customerId))
                throw new BusinessRuleException("Transfer wallets are no longer available for reversal.", "transfer_wallet_missing");

            var walletsById = wallets.ToDictionary(w => w.WalletId);
            foreach (var leg in pair)
            {
                var wallet = walletsById[leg.WalletId];
                var reversedBalance = (wallet.Balance ?? 0m) + ReverseBalanceDelta(leg.TransactionType, leg.Amount);
                if (reversedBalance < 0)
                    throw new BusinessRuleException(
                        "Transfer cannot be deleted because one receiving wallet no longer has the transferred balance.",
                        "transfer_reversal_insufficient_balance");

                wallet.Balance = reversedBalance;
                wallet.UpdatedAt = DateTime.UtcNow;
            }

            _context.Transactions.RemoveRange(pair);
        }
        else
        {
            var wallet = (await LockWalletsAsync(new[] { target.WalletId }, cancellationToken)).SingleOrDefault();
            if (wallet is null || wallet.CustomerId != customerId)
                return false;

            var reversedBalance = (wallet.Balance ?? 0m) + ReverseBalanceDelta(target.TransactionType, target.Amount);
            if (reversedBalance < 0)
                throw new BusinessRuleException("Transaction cannot be deleted because its balance has already been spent.", "reversal_insufficient_balance");

            wallet.Balance = reversedBalance;
            wallet.UpdatedAt = DateTime.UtcNow;
            _context.Transactions.Remove(target);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
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
        return MapToDto(transaction);
    }

    private static string NormalizeType(string type)
        => type.Trim().ToLowerInvariant() switch
        {
            "income" or "in" => "income",
            "expense" or "out" => "expense",
            "transfer_out" => "transfer_out",
            "transfer_in" => "transfer_in",
            "INCOME" => "income",
            "EXPENSE" => "expense",
            "TRANSFER" => "transfer_out",
            _ => type.Trim().ToLowerInvariant()
        };

    private async Task<List<Wallet>> LockWalletsAsync(Guid[] walletIds, CancellationToken cancellationToken)
    {
        if (walletIds.Length == 0)
            return new List<Wallet>();

        return await _context.Wallets
            .FromSqlInterpolated($"""
                SELECT id, customer_id, name, type, balance, is_deleted, created_at, updated_at
                FROM wallets
                WHERE id = ANY({walletIds})
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    private static decimal ReverseBalanceDelta(string transactionType, decimal amount)
        => NormalizeType(transactionType) switch
        {
            "income" => -amount,
            "expense" => amount,
            "transfer_out" => amount,
            "transfer_in" => -amount,
            _ => throw new BusinessRuleException($"Unsupported transaction type '{transactionType}'.", "transaction_type_invalid")
        };

    private static TransactionResponseDto MapToDto(Transaction transaction) => new()
    {
        TransactionId = transaction.TransactionId,
        CustomerId = transaction.CustomerId,
        WalletId = transaction.WalletId,
        CategoryId = transaction.CategoryId,
        TransactionType = transaction.TransactionType,
        SourceChannel = transaction.EntryMethod,
        EntryMethod = transaction.EntryMethod,
        Amount = transaction.Amount,
        TransactionDate = transaction.TransactionDate ?? DateTime.UtcNow,
        Note = transaction.Description,
        Description = transaction.Description,
        Merchant = transaction.Merchant,
        TransferPairId = transaction.TransferPairId,
        ExternalId = transaction.ExternalId,
        CreatedAt = transaction.CreatedAt == default ? transaction.TransactionDate ?? DateTime.UtcNow : transaction.CreatedAt,
        UpdatedAt = transaction.UpdatedAt
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
