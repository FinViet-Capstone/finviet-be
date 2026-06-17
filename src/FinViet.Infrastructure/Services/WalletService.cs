using System.Data;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = FinViet.Application.Common.Exceptions.BusinessRuleException;

namespace FinViet.Infrastructure.Services;

public class WalletService : IWalletService
{
    private const string BasicWalletType = "basic";
    private const string LinkedWalletType = "sepay_linked";
    private const int MaximumWalletsPerCustomer = 10;

    private static readonly HashSet<string> AllowedWalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BasicWalletType, LinkedWalletType, "CASH", "BANK_ACCOUNT", "E_WALLET", "INVESTMENT", "CREDIT_CARD"
    };

    private readonly FinVietDbContext _dbContext;

    public WalletService(FinVietDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WalletListResponse> GetWalletsAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var wallets = await _dbContext.Wallets
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId && !w.IsDeleted)
            .OrderBy(w => w.WalletName)
            .Select(w => new WalletResponse
            {
                WalletId = w.WalletId,
                CustomerId = customerId,
                WalletName = w.WalletName,
                WalletType = w.WalletType,
                Balance = w.Balance ?? 0m
            })
            .ToListAsync(cancellationToken);

        foreach (var wallet in wallets)
            wallet.WalletType = NormalizeStoredWalletType(wallet.WalletType);

        return new WalletListResponse
        {
            TotalBalance = wallets.Sum(x => x.Balance),
            Wallets = wallets
        };
    }

    public async Task<WalletResponse?> GetWalletByIdAsync(Guid customerId, Guid walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w =>
                w.CustomerId == customerId &&
                w.WalletId == walletId &&
                !w.IsDeleted,
                cancellationToken);

        return wallet is null ? null : ToResponse(wallet);
    }

    public async Task<WalletResponse> CreateWalletAsync(Guid customerId, CreateWalletRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreate(request);
        await EnsureCustomerExistsAsync(customerId, cancellationToken);

        var walletCount = await _dbContext.Wallets
            .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted, cancellationToken);

        if (walletCount >= MaximumWalletsPerCustomer)
            throw new ValidationException("Maximum 10 wallets allowed per account.");

        var trimmedName = request.EffectiveName.Trim();
        var isDuplicateName = await WalletNameExistsAsync(customerId, trimmedName, null, cancellationToken);

        if (isDuplicateName)
            throw new ValidationException("Wallet name already exists for this customer.");

        var wallet = new Wallet
        {
            WalletId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletName = trimmedName,
            WalletType = NormalizeWalletType(request.EffectiveType),
            Balance = request.InitialBalance
        };

        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(wallet);
    }

    public async Task<WalletResponse?> UpdateWalletAsync(Guid customerId, Guid walletId, UpdateWalletRequest request, CancellationToken cancellationToken = default)
    {
        ValidateUpdate(request);

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(w =>
                w.CustomerId == customerId &&
                w.WalletId == walletId &&
                !w.IsDeleted,
                cancellationToken);

        if (wallet is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.EffectiveName))
        {
            var newName = request.EffectiveName.Trim();
            var duplicateName = await WalletNameExistsAsync(customerId, newName, walletId, cancellationToken);

            if (duplicateName)
                throw new ValidationException("Wallet name already exists for this customer.");

            wallet.WalletName = newName;
        }

        if (!string.IsNullOrWhiteSpace(request.WalletType))
            throw new ValidationException("Wallet type cannot be changed after creation.");

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(wallet);
    }

    public async Task<bool> DeleteWalletAsync(Guid customerId, Guid walletId, CancellationToken cancellationToken = default)
    {
        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(w =>
                w.CustomerId == customerId &&
                w.WalletId == walletId &&
                !w.IsDeleted,
                cancellationToken);

        if (wallet is null)
            return false;

        var activeWalletCount = await _dbContext.Wallets
            .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted, cancellationToken);

        if (activeWalletCount <= 1)
            throw new BusinessRuleException("Cannot delete the last active wallet.", "last_wallet");

        wallet.IsDeleted = true;
        wallet.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TransferWalletResponse> TransferAsync(Guid customerId, TransferWalletRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FromWalletId == request.ToWalletId)
            throw new ValidationException("From wallet and to wallet must be different.");

        if (request.Amount <= 0)
            throw new ValidationException("Transfer amount must be greater than 0.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // Lock both wallet rows in a deterministic order to avoid overspending and transfer deadlocks.
        var wallets = await _dbContext.Wallets
            .FromSqlInterpolated($"""
                SELECT wallet_id, customer_id, wallet_name, wallet_type, balance, is_deleted, deleted_at
                FROM wallet
                WHERE customer_id = {customerId}
                  AND wallet_id IN ({request.FromWalletId}, {request.ToWalletId})
                  AND is_deleted = false
                ORDER BY wallet_id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var fromWallet = wallets.FirstOrDefault(w => w.WalletId == request.FromWalletId);
        var toWallet = wallets.FirstOrDefault(w => w.WalletId == request.ToWalletId);

        if (fromWallet is null)
            throw new NotFoundException("From wallet not found.");

        if (toWallet is null)
            throw new NotFoundException("To wallet not found.");

        var fromWalletBalance = GetRequiredBalance(fromWallet);
        var toWalletBalance = GetRequiredBalance(toWallet);

        if (fromWalletBalance < request.Amount)
            throw new ValidationException("Source wallet does not have enough balance.");

        fromWallet.Balance = fromWalletBalance - request.Amount;
        toWallet.Balance = toWalletBalance + request.Amount;

        var now = DateTime.UtcNow;
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Transfer from {fromWallet.WalletName} to {toWallet.WalletName}"
            : request.Description.Trim();

        // 2 vế cùng transfer_pair_id; category_id=null → loại khỏi mọi thống kê chi tiêu/budget/score.
        var transferPairId = Guid.NewGuid();
        _dbContext.Transactions.AddRange(
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = fromWallet.WalletId,
                CategoryId = null,
                TransactionType = "TRANSFER_OUT",
                SourceChannel = "MANUAL",
                Amount = request.Amount,
                TransactionDate = now,
                TransferPairId = transferPairId,
                BeneficiaryName = null,
                Note = $"OUT: {description}"
            },
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = toWallet.WalletId,
                CategoryId = null,
                TransactionType = "TRANSFER_IN",
                SourceChannel = "MANUAL",
                Amount = request.Amount,
                TransactionDate = now,
                TransferPairId = transferPairId,
                BeneficiaryName = null,
                Note = $"IN: {description}"
            });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TransferWalletResponse
        {
            FromWalletId = fromWallet.WalletId,
            ToWalletId = toWallet.WalletId,
            FromWalletBalance = GetRequiredBalance(fromWallet),
            ToWalletBalance = GetRequiredBalance(toWallet)
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
            .AnyAsync(x =>
                x.CustomerId == customerId &&
                x.WalletId == walletId &&
                !x.IsDeleted,
                cancellationToken);

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

        if (query.CategoryId.HasValue)
        {
            transactionsQuery = transactionsQuery
                .Where(x => x.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.TransactionType))
        {
            var type = query.TransactionType.Trim().ToUpperInvariant();
            var allowedTypes = new[]
            {
                "INCOME",
                "EXPENSE",
                "TRANSFER",
                "TRANSFER_OUT",
                "TRANSFER_IN",
                "DEBT_PAYMENT"
            };

            if (!allowedTypes.Contains(type))
            {
                throw new ValidationException(
                    "Transaction type must be one of: INCOME, EXPENSE, TRANSFER, TRANSFER_OUT, TRANSFER_IN, DEBT_PAYMENT.");
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
                WalletId = x.WalletId,
                CategoryId = x.CategoryId,
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
        if (string.IsNullOrWhiteSpace(request.EffectiveName))
            throw new ValidationException("Wallet name is required.");

        if (string.IsNullOrWhiteSpace(request.EffectiveType))
            throw new ValidationException("Wallet type is required.");
    }

    private static void ValidateUpdate(UpdateWalletRequest request)
    {
        if ((request.Name is not null || request.WalletName is not null) && string.IsNullOrWhiteSpace(request.EffectiveName))
            throw new ValidationException("Wallet name cannot be empty.");

        if (request.WalletType is not null && string.IsNullOrWhiteSpace(request.WalletType))
            throw new ValidationException("Wallet type cannot be changed after creation.");
    }

    private static string NormalizeWalletType(string walletType)
    {
        var normalized = walletType.Trim();
        if (!AllowedWalletTypes.Contains(normalized))
            throw new ValidationException("Wallet type must be one of: basic, sepay_linked.");

        return normalized.ToUpperInvariant() switch
        {
            "CASH" or "BANK_ACCOUNT" or "E_WALLET" or "INVESTMENT" or "CREDIT_CARD" => BasicWalletType,
            _ => normalized.ToLowerInvariant()
        };
    }

    private static WalletResponse ToResponse(Wallet wallet)
        => new()
        {
            WalletId = wallet.WalletId,
            CustomerId = GetRequiredCustomerId(wallet),
            WalletName = wallet.WalletName,
            WalletType = NormalizeStoredWalletType(wallet.WalletType),
            Balance = GetRequiredBalance(wallet)
        };

    private static Guid GetRequiredCustomerId(Wallet wallet)
        => wallet.CustomerId
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing customer_id.");

    private static decimal GetRequiredBalance(Wallet wallet)
        => wallet.Balance
           ?? throw new InvalidOperationException($"Wallet {wallet.WalletId} is missing balance.");

    private async Task<bool> WalletNameExistsAsync(
        Guid customerId,
        string walletName,
        Guid? excludedWalletId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Wallets.AnyAsync(
            w => w.CustomerId == customerId
                 && !w.IsDeleted
                 && (!excludedWalletId.HasValue || w.WalletId != excludedWalletId.Value)
                 && EF.Functions.ILike(w.WalletName, walletName),
            cancellationToken);
    }

    private static string NormalizeStoredWalletType(string walletType)
        => walletType.ToUpperInvariant() switch
        {
            "CASH" or "BANK_ACCOUNT" or "E_WALLET" or "INVESTMENT" or "CREDIT_CARD" => BasicWalletType,
            _ => walletType.ToLowerInvariant()
        };
}
