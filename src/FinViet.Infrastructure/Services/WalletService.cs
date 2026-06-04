using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class WalletService : IWalletService
{
    private static readonly HashSet<string> AllowedWalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CASH", "BANK_ACCOUNT", "CREDIT_CARD", "E_WALLET", "INVESTMENT"
    };

    private readonly FinVietDbContext _dbContext;

    public WalletService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WalletResponse>> GetWalletsAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Wallets
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .OrderBy(w => w.WalletName)
            .Select(w => new WalletResponse
            {
                WalletId = w.WalletId,
                CustomerId = w.CustomerId ?? Guid.Empty,
                WalletName = w.WalletName,
                WalletType = w.WalletType,
                Balance = w.Balance ?? 0m
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<WalletResponse?> GetWalletByIdAsync(Guid customerId, Guid walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId, cancellationToken);

        return wallet is null ? null : ToResponse(wallet);
    }

    public async Task<WalletResponse> CreateWalletAsync(Guid customerId, CreateWalletRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreate(request);
        await EnsureCustomerExistsAsync(customerId, cancellationToken);

        var trimmedName = request.WalletName.Trim();
        var isDuplicateName = await _dbContext.Wallets
            .AnyAsync(w => w.CustomerId == customerId && w.WalletName.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (isDuplicateName)
            throw new ValidationException("Wallet name already exists for this customer.");

        var wallet = new Wallet
        {
            WalletId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletName = trimmedName,
            WalletType = NormalizeWalletType(request.WalletType),
            Balance = request.InitialBalance
        };

        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(wallet);
    }

    public async Task<WalletResponse?> UpdateWalletAsync(Guid customerId, Guid walletId, UpdateWalletRequest request, CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId, cancellationToken);

        if (wallet is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.WalletName))
        {
            var newName = request.WalletName.Trim();
            var duplicateName = await _dbContext.Wallets.AnyAsync(w =>
                w.CustomerId == customerId &&
                w.WalletId != walletId &&
                w.WalletName.ToLower() == newName.ToLower(), cancellationToken);

            if (duplicateName)
                throw new ValidationException("Wallet name already exists for this customer.");

            wallet.WalletName = newName;
        }

        if (!string.IsNullOrWhiteSpace(request.WalletType))
            wallet.WalletType = NormalizeWalletType(request.WalletType);

        if (request.Balance.HasValue)
        {
            if (request.Balance.Value < 0 && !wallet.WalletType.Equals("CREDIT_CARD", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("Balance cannot be negative except for CREDIT_CARD wallets.");

            wallet.Balance = request.Balance.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(wallet);
    }

    public async Task<bool> DeleteWalletAsync(Guid customerId, Guid walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId, cancellationToken);

        if (wallet is null)
            return false;

        var hasTransactions = await _dbContext.Transactions
            .AnyAsync(t => t.WalletId == walletId, cancellationToken);

        if (hasTransactions)
            throw new ValidationException("Cannot delete wallet because it already has transactions.");

        _dbContext.Wallets.Remove(wallet);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TransferWalletResponse> TransferAsync(Guid customerId, TransferWalletRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FromWalletId == request.ToWalletId)
            throw new ValidationException("From wallet and to wallet must be different.");

        if (request.Amount <= 0)
            throw new ValidationException("Transfer amount must be greater than 0.");

        var wallets = await _dbContext.Wallets
            .Where(w => w.CustomerId == customerId && (w.WalletId == request.FromWalletId || w.WalletId == request.ToWalletId))
            .ToListAsync(cancellationToken);

        var fromWallet = wallets.FirstOrDefault(w => w.WalletId == request.FromWalletId);
        var toWallet = wallets.FirstOrDefault(w => w.WalletId == request.ToWalletId);

        if (fromWallet is null)
            throw new NotFoundException("From wallet not found.");

        if (toWallet is null)
            throw new NotFoundException("To wallet not found.");

        var fromBalance = fromWallet.Balance ?? 0m;
        var toBalance = toWallet.Balance ?? 0m;

        if (fromBalance < request.Amount && !fromWallet.WalletType.Equals("CREDIT_CARD", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Source wallet does not have enough balance.");

        fromWallet.Balance = fromBalance - request.Amount;
        toWallet.Balance = toBalance + request.Amount;

        var now = DateTime.UtcNow;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Transfer from {fromWallet.WalletName} to {toWallet.WalletName}"
            : request.Description.Trim();

        _dbContext.Transactions.AddRange(
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = fromWallet.WalletId,
                TransactionType = "TRANSFER",
                Amount = request.Amount,
                TransactionDate = now,
                Note = $"OUT: {description}"
            },
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = toWallet.WalletId,
                TransactionType = "TRANSFER",
                Amount = request.Amount,
                TransactionDate = now,
                Note = $"IN: {description}"
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new TransferWalletResponse
        {
            FromWalletId = fromWallet.WalletId,
            ToWalletId = toWallet.WalletId,
            FromWalletBalance = fromWallet.Balance ?? 0m,
            ToWalletBalance = toWallet.Balance ?? 0m
        };
    }

    public async Task<PagedResult<WalletTransactionResponse>> GetWalletTransactionsAsync(
        Guid customerId,
        Guid walletId,
        WalletTransactionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Page <= 0)
            throw new ValidationException("Page must be greater than 0.");

        if (query.PageSize <= 0 || query.PageSize > 100)
            throw new ValidationException("Page size must be between 1 and 100.");

        var walletExists = await _dbContext.Wallets
            .AnyAsync(x => x.CustomerId == customerId && x.WalletId == walletId, cancellationToken);

        if (!walletExists)
            throw new NotFoundException("Wallet not found.");

        var transactionsQuery = _dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.WalletId == walletId);

        if (query.FromDate.HasValue)
        {
            transactionsQuery = transactionsQuery
                .Where(x => x.TransactionDate >= query.FromDate.Value.UtcDateTime);
        }

        if (query.ToDate.HasValue)
        {
            transactionsQuery = transactionsQuery
                .Where(x => x.TransactionDate <= query.ToDate.Value.UtcDateTime);
        }

        if (!string.IsNullOrWhiteSpace(query.TransactionType))
        {
            var type = query.TransactionType.Trim().ToUpperInvariant();
            var allowedTypes = new[]
            {
                "INCOME",
                "EXPENSE",
                "TRANSFER",
                "DEBT_PAYMENT"
            };

            if (!allowedTypes.Contains(type))
            {
                throw new ValidationException(
                    "Transaction type must be one of: INCOME, EXPENSE, TRANSFER, DEBT_PAYMENT.");
            }

            transactionsQuery = transactionsQuery
                .Where(x => x.TransactionType == type);
        }

        transactionsQuery = query.SortOrder.Trim().ToLowerInvariant() == "asc"
            ? transactionsQuery.OrderBy(x => x.TransactionDate)
            : transactionsQuery.OrderByDescending(x => x.TransactionDate);

        var totalItems = await transactionsQuery.CountAsync(cancellationToken);

        var items = await transactionsQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new WalletTransactionResponse
            {
                TransactionId = x.TransactionId,
                WalletId = x.WalletId ?? Guid.Empty,
                CategoryId = x.CategoryId,
                SourceId = x.SourceId,
                BatchId = x.BatchId,
                ReportId = x.ReportId,
                TransactionType = x.TransactionType,
                Amount = x.Amount,
                TransactionDate = x.TransactionDate.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(x.TransactionDate.Value, DateTimeKind.Utc))
                    : DateTimeOffset.MinValue,
                Note = x.Note
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<WalletTransactionResponse>
        {
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize),
            Items = items
        };
    }

    private async Task EnsureCustomerExistsAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!exists)
            throw new NotFoundException("Customer not found.");
    }

    private static void ValidateCreate(CreateWalletRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WalletName))
            throw new ValidationException("Wallet name is required.");

        if (string.IsNullOrWhiteSpace(request.WalletType))
            throw new ValidationException("Wallet type is required.");

        if (request.InitialBalance < 0 && !request.WalletType.Trim().Equals("CREDIT_CARD", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Initial balance cannot be negative except for CREDIT_CARD wallets.");
    }

    private static string NormalizeWalletType(string walletType)
    {
        var normalized = walletType.Trim().ToUpperInvariant();
        if (!AllowedWalletTypes.Contains(normalized))
            throw new ValidationException("Wallet type must be one of: CASH, BANK_ACCOUNT, CREDIT_CARD, E_WALLET, INVESTMENT.");

        return normalized;
    }

    private static WalletResponse ToResponse(Wallet wallet)
        => new()
        {
            WalletId = wallet.WalletId,
            CustomerId = wallet.CustomerId ?? Guid.Empty,
            WalletName = wallet.WalletName,
            WalletType = wallet.WalletType,
            Balance = wallet.Balance ?? 0m
        };
}
