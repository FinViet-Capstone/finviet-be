using FinViet.Application.Interfaces;
using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
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

    private static readonly string[] ValidTypes = { "INCOME", "EXPENSE", "TRANSFER", "DEBT_PAYMENT" };

    // ── Read APIs ───────────────────────────────────────────────────────────────
    // Transactions carry no customer_id; ownership is enforced through the wallet
    // (only rows whose wallet belongs to the authenticated customer are visible).

    public async Task<PagedResult<TransactionResponseDto>> GetPagedAsync(
        Guid customerId, TransactionQueryDto filter, CancellationToken cancellationToken = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

        var query = _context.Transactions
            .AsNoTracking()
            .Where(t => t.Wallet != null && t.Wallet.CustomerId == customerId);

        if (filter.WalletId.HasValue)
            query = query.Where(t => t.WalletId == filter.WalletId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            var type = filter.Type.Trim().ToUpperInvariant();
            if (!ValidTypes.Contains(type))
                throw new BadRequestException($"type must be one of: {string.Join(", ", ValidTypes)}.");
            query = query.Where(t => t.TransactionType == type);
        }

        if (filter.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == filter.CategoryId.Value);

        if (filter.From.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.To.Value);

        if (filter.UncategorizedOnly)
            query = query.Where(t => t.CategoryId == null);

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = $"%{filter.Q.Trim()}%";
            query = query.Where(t =>
                (t.Note != null && EF.Functions.ILike(t.Note, term)) ||
                (t.BeneficiaryName != null && EF.Functions.ILike(t.BeneficiaryName, term)));
        }

        var total = await query.CountAsync(cancellationToken);

        // Materialize first, then map in memory — EF cannot translate MapToDto into SQL.
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
                        && t.Wallet != null && t.Wallet.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        return transaction is null ? null : MapToDto(transaction);
    }

    public async Task<TransactionSummaryResponseDto> GetSummaryAsync(
        Guid customerId, int year, int month, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        // Transfers are excluded from income/expense aggregates.
        var rows = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.Wallet != null && t.Wallet.CustomerId == customerId
                        && t.TransactionType != "TRANSFER"
                        && t.TransactionDate >= start && t.TransactionDate < end)
            .Select(t => new
            {
                t.TransactionType,
                t.Amount,
                t.CategoryId,
                t.BeneficiaryName,
                t.TransactionDate
            })
            .ToListAsync(cancellationToken);

        var income = rows.Where(r => r.TransactionType == "INCOME").Sum(r => r.Amount);
        var expense = rows.Where(r => r.TransactionType == "EXPENSE").Sum(r => r.Amount);

        var categoryNames = await _context.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, cancellationToken);

        var byCategory = rows
            .Where(r => r.TransactionType == "EXPENSE")
            .GroupBy(r => r.CategoryId)
            .Select(g => new CategorySummaryItemDto
            {
                CategoryId = g.Key,
                CategoryName = g.Key.HasValue && categoryNames.TryGetValue(g.Key.Value, out var name) ? name : null,
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
                Income = g.Where(x => x.TransactionType == "INCOME").Sum(x => x.Amount),
                Expense = g.Where(x => x.TransactionType == "EXPENSE").Sum(x => x.Amount),
                Net = g.Where(x => x.TransactionType == "INCOME").Sum(x => x.Amount)
                      - g.Where(x => x.TransactionType == "EXPENSE").Sum(x => x.Amount)
            })
            .OrderBy(d => d.Date)
            .ToList();

        var topBeneficiaries = rows
            .Where(r => r.TransactionType == "EXPENSE" && !string.IsNullOrWhiteSpace(r.BeneficiaryName))
            .GroupBy(r => r.BeneficiaryName!)
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

    private static TransactionResponseDto MapToDto(Transaction transaction) => new()
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

