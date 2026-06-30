using System.Data;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Wallets;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = FinViet.Application.Common.Exceptions.BusinessRuleException;

namespace FinViet.Infrastructure.Services;

public class WalletService : IWalletService
{
    private const string BasicWalletType = "basic";
    private const string SepayLinkedWalletType = "sepay_linked";
    private const int MaximumWalletsPerCustomer = 10;

    private static readonly HashSet<string> AllowedWalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BasicWalletType, SepayLinkedWalletType, "CASH", "BANK_ACCOUNT", "CREDIT_CARD", "E_WALLET", "INVESTMENT"
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
                WalletType = NormalizeStoredWalletType(w.WalletType),
                Balance = w.Balance ?? 0m
            })
            .ToListAsync(cancellationToken);

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
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId && !w.IsDeleted, cancellationToken);

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

        var trimmedName = request.WalletName.Trim();
        var isDuplicateName = await WalletNameExistsAsync(customerId, trimmedName, null, cancellationToken);

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
        ValidateUpdate(request);

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId && !w.IsDeleted, cancellationToken);

        if (wallet is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.WalletName))
        {
            var newName = request.WalletName.Trim();
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
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.WalletId == walletId && !w.IsDeleted, cancellationToken);

        if (wallet is null)
            return false;

        // Không cho xóa ví active cuối cùng (APIs-List §3: 422 last_wallet).
        var activeWalletCount = await _dbContext.Wallets
            .CountAsync(w => w.CustomerId == customerId && !w.IsDeleted, cancellationToken);

        if (activeWalletCount <= 1)
            throw new BusinessRuleException("Cannot delete the last active wallet.", "last_wallet");

        // Soft delete — giữ lịch sử giao dịch (schema v3: wallets.is_deleted).
        wallet.IsDeleted = true;
        wallet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TransferWalletResponse> TransferAsync(
        Guid customerId,
        TransferWalletRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request.FromWalletId == request.ToWalletId)
            throw new ValidationException("From wallet and to wallet must be different.");

        if (request.Amount <= 0)
            throw new ValidationException("Transfer amount must be greater than 0.");

        return await ExecuteWalletTransferAsync(
            customerId,
            request.FromWalletId,
            request.ToWalletId,
            request.Amount,
            request.Description,
            "Transfer",
            "wallet-transfer",
            idempotencyKey,
            cancellationToken);
    }

    public async Task<TransferWalletResponse> WithdrawAsync(
        Guid customerId,
        WithdrawWalletRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ValidationException("Withdrawal amount must be greater than 0.");

        // Source must be a SePay-linked wallet owned by the customer (rút tiền: sepay_linked → basic).
        var sourceWallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.CustomerId == customerId && w.WalletId == request.FromWalletId && !w.IsDeleted,
                cancellationToken);

        if (sourceWallet is null)
            throw new NotFoundException("Source wallet not found.");

        if (!NormalizeStoredWalletType(sourceWallet.WalletType).Equals(SepayLinkedWalletType, StringComparison.Ordinal))
            throw new BusinessRuleException(
                "Withdrawal is only allowed from a SePay-linked wallet.",
                "withdraw_source_not_sepay");

        // Destination is a basic wallet: auto-pick when there is exactly one, otherwise prompt for a choice.
        var destinationWalletId = await ResolveWithdrawDestinationAsync(
            customerId,
            request.ToWalletId,
            request.FromWalletId,
            cancellationToken);

        return await ExecuteWalletTransferAsync(
            customerId,
            request.FromWalletId,
            destinationWalletId,
            request.Amount,
            request.Description,
            "Withdrawal",
            "wallet-withdraw",
            idempotencyKey,
            cancellationToken);
    }

    // Moves money between two wallets of the same customer as a transfer pair
    // (transfer_out + transfer_in sharing one transfer_pair_id, category/merchant null) so the
    // movement is excluded from spending/budget/score and a deleted leg can drop its partner.
    private async Task<TransferWalletResponse> ExecuteWalletTransferAsync(
        Guid customerId,
        Guid fromWalletId,
        Guid toWalletId,
        decimal amount,
        string? description,
        string transferLabel,
        string idempotencyOperation,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var requestHash = IdempotencyStore.ComputeRequestHash(new
        {
            fromWalletId,
            toWalletId,
            amount,
            description,
            transferLabel
        });
        var idempotency = await IdempotencyStore.ClaimAsync(
            _dbContext,
            customerId,
            idempotencyOperation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (idempotency.IsReplay)
        {
            await transaction.CommitAsync(cancellationToken);
            return IdempotencyStore.ReadReplay<TransferWalletResponse>(idempotency);
        }

        // Lock both wallet rows in a deterministic order to avoid overspending and transfer deadlocks.
        // FromSql must SELECT every mapped column of the Wallet entity (table: wallets).
        var wallets = await _dbContext.Wallets
            .FromSqlInterpolated($"""
                SELECT id, customer_id, name, type, balance, is_deleted, created_at, updated_at
                FROM wallets
                WHERE customer_id = {customerId}
                  AND id IN ({fromWalletId}, {toWalletId})
                  AND is_deleted = false
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var fromWallet = wallets.FirstOrDefault(w => w.WalletId == fromWalletId);
        var toWallet = wallets.FirstOrDefault(w => w.WalletId == toWalletId);

        if (fromWallet is null)
            throw new NotFoundException("From wallet not found.");

        if (toWallet is null)
            throw new NotFoundException("To wallet not found.");

        var fromWalletBalance = GetRequiredBalance(fromWallet);
        var toWalletBalance = GetRequiredBalance(toWallet);

        if (fromWalletBalance < amount)
            throw new ValidationException("Source wallet does not have enough balance.");

        var now = DateTime.UtcNow;
        fromWallet.Balance = fromWalletBalance - amount;
        toWallet.Balance = toWalletBalance + amount;
        fromWallet.UpdatedAt = now;
        toWallet.UpdatedAt = now;

        var note = string.IsNullOrWhiteSpace(description)
            ? $"{transferLabel} from {fromWallet.WalletName} to {toWallet.WalletName}"
            : description.Trim();
        var transferPairId = Guid.NewGuid();

        _dbContext.Transactions.AddRange(
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = customerId,
                WalletId = fromWallet.WalletId,
                CategoryId = null,
                TransactionType = "transfer_out",
                EntryMethod = "manual",
                Amount = amount,
                Merchant = null,
                Description = note,
                TransactionDate = now,
                TransferPairId = transferPairId,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = customerId,
                WalletId = toWallet.WalletId,
                CategoryId = null,
                TransactionType = "transfer_in",
                EntryMethod = "manual",
                Amount = amount,
                Merchant = null,
                Description = note,
                TransactionDate = now,
                TransferPairId = transferPairId,
                CreatedAt = now,
                UpdatedAt = now
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = new TransferWalletResponse
        {
            FromWalletId = fromWallet.WalletId,
            ToWalletId = toWallet.WalletId,
            FromWalletBalance = GetRequiredBalance(fromWallet),
            ToWalletBalance = GetRequiredBalance(toWallet)
        };
        await IdempotencyStore.CompleteAsync(
            _dbContext,
            customerId,
            idempotencyOperation,
            idempotencyKey!,
            result,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    // Picks the basic wallet that receives a withdrawal. With one basic wallet it is implicit;
    // with several the caller must name one (FE prompts on the 422 "multiple_basic_wallets").
    private async Task<Guid> ResolveWithdrawDestinationAsync(
        Guid customerId,
        Guid? requestedWalletId,
        Guid sourceWalletId,
        CancellationToken cancellationToken)
    {
        var basicWalletIds = (await _dbContext.Wallets
                .AsNoTracking()
                .Where(w => w.CustomerId == customerId && !w.IsDeleted)
                .Select(w => new { w.WalletId, w.WalletType })
                .ToListAsync(cancellationToken))
            .Where(w => NormalizeStoredWalletType(w.WalletType).Equals(BasicWalletType, StringComparison.Ordinal))
            .Select(w => w.WalletId)
            .ToList();

        if (basicWalletIds.Count == 0)
            throw new BusinessRuleException(
                "No basic wallet is available to receive the withdrawal. Create a basic wallet first.",
                "no_basic_wallet");

        if (requestedWalletId.HasValue)
        {
            if (requestedWalletId.Value == sourceWalletId)
                throw new ValidationException("Destination wallet must be different from the source wallet.");

            if (!basicWalletIds.Contains(requestedWalletId.Value))
                throw new ValidationException("Destination wallet must be an existing basic wallet.");

            return requestedWalletId.Value;
        }

        if (basicWalletIds.Count > 1)
            throw new BusinessRuleException(
                "Multiple basic wallets found. Specify which wallet should receive the withdrawal.",
                "multiple_basic_wallets");

        return basicWalletIds[0];
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
            .AnyAsync(x => x.CustomerId == customerId && x.WalletId == walletId && !x.IsDeleted, cancellationToken);

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

        if (!string.IsNullOrWhiteSpace(query.CategoryId))
        {
            transactionsQuery = transactionsQuery
                .Where(x => x.CategoryId == query.CategoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.TransactionType))
        {
            var type = query.TransactionType.Trim().ToLowerInvariant();
            var allowedTypes = new[]
            {
                "income",
                "expense",
                "transfer_out",
                "transfer_in"
            };

            if (!allowedTypes.Contains(type))
            {
                throw new ValidationException(
                    "Transaction type must be one of: income, expense, transfer_out, transfer_in.");
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
                Note = x.Description
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

        if (request.InitialBalance < 0)
            throw new ValidationException("Initial balance cannot be negative.");
    }

    private static void ValidateUpdate(UpdateWalletRequest request)
    {
        if (request.WalletName is not null && string.IsNullOrWhiteSpace(request.WalletName))
            throw new ValidationException("Wallet name cannot be empty.");

        if (request.WalletType is not null)
            throw new ValidationException("Wallet type cannot be changed after creation.");
    }

    private static string NormalizeWalletType(string walletType)
    {
        var normalized = walletType.Trim();
        if (!AllowedWalletTypes.Contains(normalized))
            throw new ValidationException("Wallet type must be one of: basic, sepay_linked.");

        return normalized.ToUpperInvariant() switch
        {
            "CASH" or "BANK_ACCOUNT" or "CREDIT_CARD" or "E_WALLET" or "INVESTMENT" => BasicWalletType,
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
            "CASH" or "BANK_ACCOUNT" or "CREDIT_CARD" or "E_WALLET" or "INVESTMENT" => BasicWalletType,
            _ => walletType.ToLowerInvariant()
        };
}
