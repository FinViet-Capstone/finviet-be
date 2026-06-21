using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
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
            if (!TryParseType(filter.Type, out var typeFilter))
                throw new BadRequestException("type must be one of: expense, income, transfer_out, transfer_in.");
            query = query.Where(t => t.TransactionType == typeFilter);
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryId))
            query = query.Where(t => t.CategoryId == filter.CategoryId);

        if (filter.From.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(t => t.TransactionDate <= filter.To.Value);

        if (filter.UncategorizedOnly)
            query = query.Where(t => t.CategoryId == null
                && t.TransactionType != TransactionType.TransferOut
                && t.TransactionType != TransactionType.TransferIn);

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
                        && t.TransactionType != TransactionType.TransferOut
                        && t.TransactionType != TransactionType.TransferIn
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

        var income = rows.Where(r => r.TransactionType == TransactionType.Income).Sum(r => r.Amount);
        var expense = rows.Where(r => r.TransactionType == TransactionType.Expense).Sum(r => r.Amount);

        var categoryNames = await _context.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, cancellationToken);

        var byCategory = rows
            .Where(r => r.TransactionType == TransactionType.Expense)
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
                Income = g.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount),
                Expense = g.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount),
                Net = g.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount)
                      - g.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount)
            })
            .OrderBy(d => d.Date)
            .ToList();

        var topBeneficiaries = rows
            .Where(r => r.TransactionType == TransactionType.Expense && !string.IsNullOrWhiteSpace(r.Merchant))
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
            EntryMethod = EntryMethod.Manual,
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

    /// <summary>Parses an API string into the transaction_type enum. Accepts legacy aliases.</summary>
    private static bool TryParseType(string raw, out TransactionType type)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "income" or "in": type = TransactionType.Income; return true;
            case "expense" or "out": type = TransactionType.Expense; return true;
            case "transfer_out" or "transfer": type = TransactionType.TransferOut; return true;
            case "transfer_in": type = TransactionType.TransferIn; return true;
            default: type = default; return false;
        }
    }

    private static TransactionType NormalizeType(string type)
        => TryParseType(type, out var parsed)
            ? parsed
            : throw new BadRequestException("type must be one of: expense, income, transfer_out, transfer_in.");

    private static string ToTypeString(TransactionType type) => type switch
    {
        TransactionType.Income => "income",
        TransactionType.Expense => "expense",
        TransactionType.TransferOut => "transfer_out",
        TransactionType.TransferIn => "transfer_in",
        _ => "expense"
    };

    private static string ToEntryMethodString(EntryMethod method) => method switch
    {
        EntryMethod.Manual => "manual",
        EntryMethod.Photo => "photo",
        EntryMethod.SmsPaste => "sms_paste",
        EntryMethod.CsvImport => "csv_import",
        EntryMethod.SepaySync => "sepay_sync",
        _ => "manual"
    };

    private static TransactionResponseDto MapToDto(Transaction transaction) => new()
    {
        TransactionId = transaction.TransactionId,
        CustomerId = transaction.CustomerId,
        WalletId = transaction.WalletId,
        CategoryId = transaction.CategoryId,
        TransactionType = ToTypeString(transaction.TransactionType),
        SourceChannel = ToEntryMethodString(transaction.EntryMethod),
        EntryMethod = ToEntryMethodString(transaction.EntryMethod),
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
            WalletType = wallet.WalletType == FinViet.Domain.Enums.WalletType.SepayLinked ? "sepay_linked" : "basic",
            Balance = GetRequiredBalance(wallet)
        };
    }

    public async Task<WalletDto> UpdateBalanceAsync(Guid id, decimal newBalance, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { id }, cancellationToken: cancellationToken);
        if (wallet == null)
            return null;

        // Entity is already tracked by FindAsync; set only Balance so EF writes a single
        // column. Calling Update(wallet) would mark every column modified — including the
        // wallet_type enum — and EF would send it as text, failing the enum cast.
        wallet.Balance = newBalance;
        await _context.SaveChangesAsync(cancellationToken);

        return new WalletDto
        {
            WalletId = wallet.WalletId,
            CustomerId = GetRequiredCustomerId(wallet),
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType == FinViet.Domain.Enums.WalletType.SepayLinked ? "sepay_linked" : "basic",
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
