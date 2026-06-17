using System.Data;
using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Transactions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class TransactionService : ITransactionService
{
    private const string GoalFundingCategoryId = "cat_savings_goal";
    private const string SepayEntryMethod = "sepay_sync";
    private const int MaxBatchItems = 5;

    private static readonly HashSet<string> ManualTypes = new(StringComparer.Ordinal) { "expense", "income" };
    private static readonly HashSet<string> AllTypes = new(StringComparer.Ordinal)
        { "expense", "income", "transfer_out", "transfer_in" };
    private static readonly HashSet<string> AllowedEntryMethods = new(StringComparer.Ordinal)
        { "manual", "photo", "sms_paste", "csv_import", "sepay_sync" };

    private readonly FinVietDbContext _db;

    public TransactionService(FinVietDbContext db)
    {
        _db = db;
    }

    // ── Query ──────────────────────────────────────────────────────────────────────
    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(
        Guid customerId, TransactionQuery query, CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var q = _db.Transactions.AsNoTracking().Where(t => t.CustomerId == customerId);

        if (query.WalletId.HasValue)
            q = q.Where(t => t.WalletId == query.WalletId.Value);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim().ToLowerInvariant();
            if (!AllTypes.Contains(type))
                throw new BadRequestException("type must be one of: expense, income, transfer_out, transfer_in.");
            q = q.Where(t => t.TransactionType == type);
        }

        if (!string.IsNullOrWhiteSpace(query.CategoryId))
            q = q.Where(t => t.CategoryId == query.CategoryId);

        if (query.From.HasValue)
            q = q.Where(t => t.TransactionDate >= query.From.Value);
        if (query.To.HasValue)
            q = q.Where(t => t.TransactionDate <= query.To.Value);

        if (query.UncategorizedOnly)
            // Uncategorized = no category AND not a transfer leg (transfers are intentionally categoryless).
            q = q.Where(t => t.CategoryId == null && t.TransferPairId == null);

        if (query.HideGoalContributions)
            q = q.Where(t => t.CategoryId != GoalFundingCategoryId);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = $"%{query.Q.Trim()}%";
            q = q.Where(t =>
                (t.Merchant != null && EF.Functions.ILike(t.Merchant, term)) ||
                (t.Description != null && EF.Functions.ILike(t.Description, term)));
        }

        var total = await q.CountAsync(cancellationToken);

        // Stable order: transaction_date DESC, id DESC (matches the cursor contract).
        var items = await q
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.TransactionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => ToResponse(t))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionResponse>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = items
        };
    }

    public async Task<TransactionResponse?> GetByIdAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.CustomerId == customerId, cancellationToken);
        return txn is null ? null : ToResponse(txn);
    }

    // ── Create ─────────────────────────────────────────────────────────────────────
    public async Task<TransactionResponse> CreateAsync(
        Guid customerId, CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        var type = NormalizeManualType(request.Type);
        ValidateAmount(request.Amount);
        var entryMethod = NormalizeEntryMethod(request.EntryMethod);
        await ValidateCategoryForTypeAsync(request.CategoryId, type, cancellationToken);

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var wallet = await LockWalletAsync(customerId, request.WalletId, cancellationToken);

        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = wallet.WalletId,
            CategoryId = request.CategoryId,
            TransactionType = type,
            Amount = request.Amount,
            Description = request.Description,
            Merchant = request.Merchant,
            EntryMethod = entryMethod,
            TransactionDate = request.TransactionDate ?? DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(txn);

        wallet.Balance = GetBalance(wallet) + SignedDelta(type, request.Amount);

        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return ToResponse(txn);
    }

    public async Task<IReadOnlyList<TransactionResponse>> CreateBatchAsync(
        Guid customerId, BatchTransactionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0)
            throw new BadRequestException("Batch must contain at least one item.");
        if (request.Items.Count > MaxBatchItems)
            throw new BadRequestException($"Batch is limited to {MaxBatchItems} items.");

        // Validate everything up-front so the batch is all-or-nothing.
        foreach (var item in request.Items)
        {
            NormalizeManualType(item.Type);
            ValidateAmount(item.Amount);
            await ValidateCategoryForTypeAsync(item.CategoryId, item.Type.Trim().ToLowerInvariant(), cancellationToken);
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var wallet = await LockWalletAsync(customerId, request.WalletId, cancellationToken);

        var created = new List<Transaction>(request.Items.Count);
        var balance = GetBalance(wallet);
        foreach (var item in request.Items)
        {
            var type = item.Type.Trim().ToLowerInvariant();
            var txn = new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = customerId,
                WalletId = wallet.WalletId,
                CategoryId = item.CategoryId,
                TransactionType = type,
                Amount = item.Amount,
                Description = item.Description,
                Merchant = item.Merchant,
                EntryMethod = "photo",
                TransactionDate = item.TransactionDate ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Transactions.Add(txn);
            created.Add(txn);
            balance += SignedDelta(type, item.Amount);
        }

        wallet.Balance = balance;
        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return created.Select(ToResponse).ToList();
    }

    // ── Update ─────────────────────────────────────────────────────────────────────
    public async Task<TransactionResponse?> UpdateAsync(
        Guid customerId, Guid transactionId, UpdateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.CustomerId == customerId, cancellationToken);
        if (txn is null)
            return null;

        var isTransferLeg = txn.TransferPairId != null;
        var isSepay = string.Equals(txn.EntryMethod, SepayEntryMethod, StringComparison.Ordinal);

        // Transfer legs: only description is editable.
        if (isTransferLeg)
        {
            if (request.CategoryId != null || request.Amount.HasValue || request.WalletId.HasValue
                || request.Merchant != null || request.TransactionDate.HasValue)
                throw new UnprocessableEntityException(
                    "Only the description of a transfer leg can be edited.", "transfer_leg_immutable");

            if (request.Description != null)
            {
                txn.Description = request.Description;
                txn.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            await dbTx.CommitAsync(cancellationToken);
            return ToResponse(txn);
        }

        // SePay-synced: only category may change (merchant/date/amount/type/wallet immutable).
        if (isSepay && (request.Amount.HasValue || request.WalletId.HasValue
            || request.Merchant != null || request.TransactionDate.HasValue || request.Description != null))
            throw new UnprocessableEntityException(
                "SePay-synced transactions allow only category changes.", "sepay_immutable");

        // Category change (+ correction log).
        if (request.CategoryId != null)
        {
            await ValidateCategoryForTypeAsync(request.CategoryId, txn.TransactionType, cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.OriginalAiGuess)
                && !string.Equals(request.OriginalAiGuess, request.CategoryId, StringComparison.Ordinal))
            {
                _db.CategoryCorrectionLogs.Add(new CategoryCorrectionLog
                {
                    LogId = Guid.NewGuid(),
                    CustomerId = customerId,
                    TransactionId = transactionId,
                    CorrectedCategoryId = request.CategoryId,
                    OriginalAiGuess = request.OriginalAiGuess,
                    CreatedAt = DateTime.UtcNow
                });
            }
            txn.CategoryId = request.CategoryId;
        }

        if (request.Merchant != null)
            txn.Merchant = request.Merchant;
        if (request.Description != null)
            txn.Description = request.Description;
        if (request.TransactionDate.HasValue)
            txn.TransactionDate = request.TransactionDate.Value;

        // Amount / wallet changes require rebalancing wallet(s).
        var newAmount = request.Amount ?? txn.Amount;
        if (request.Amount.HasValue)
            ValidateAmount(request.Amount.Value);

        var targetWalletId = request.WalletId ?? txn.WalletId;
        var walletChanged = targetWalletId != txn.WalletId;

        if (request.Amount.HasValue || walletChanged)
        {
            var oldWallet = await LockWalletAsync(customerId, txn.WalletId, cancellationToken);
            // Reverse old effect.
            oldWallet.Balance = GetBalance(oldWallet) - SignedDelta(txn.TransactionType, txn.Amount);

            Wallet targetWallet = oldWallet;
            if (walletChanged)
                targetWallet = await LockWalletAsync(customerId, targetWalletId, cancellationToken);

            targetWallet.Balance = GetBalance(targetWallet) + SignedDelta(txn.TransactionType, newAmount);

            txn.WalletId = targetWalletId;
            txn.Amount = newAmount;
        }

        txn.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return ToResponse(txn);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────────
    public async Task<bool> DeleteAsync(
        Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.CustomerId == customerId, cancellationToken);
        if (txn is null)
        {
            await dbTx.CommitAsync(cancellationToken);
            return false;
        }

        // Goal-funding transactions are owned by the goal flow; delete via the goal, not directly.
        if (string.Equals(txn.CategoryId, GoalFundingCategoryId, StringComparison.Ordinal))
            throw new UnprocessableEntityException(
                "This transaction belongs to a savings goal; remove it via the goal.", "goal_transaction");

        if (txn.TransferPairId != null)
        {
            // Delete both legs and refund both wallets atomically.
            var legs = await _db.Transactions
                .Where(t => t.TransferPairId == txn.TransferPairId)
                .ToListAsync(cancellationToken);

            foreach (var leg in legs)
            {
                var wallet = await LockWalletAsync(customerId, leg.WalletId, cancellationToken);
                wallet.Balance = GetBalance(wallet) - SignedDelta(leg.TransactionType, leg.Amount);
            }
            _db.Transactions.RemoveRange(legs);
        }
        else
        {
            var wallet = await LockWalletAsync(customerId, txn.WalletId, cancellationToken);
            wallet.Balance = GetBalance(wallet) - SignedDelta(txn.TransactionType, txn.Amount);
            _db.Transactions.Remove(txn);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);
        return true;
    }

    // ── Transfer ───────────────────────────────────────────────────────────────────
    public async Task<TransferResponse> TransferAsync(
        Guid customerId, TransferRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FromWalletId == request.ToWalletId)
            throw new UnprocessableEntityException("Cannot transfer to the same wallet.", "same_wallet");
        if (request.Amount <= 0)
            throw new UnprocessableEntityException("Transfer amount must be greater than zero.", "invalid_amount");

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Lock both rows in a deterministic order to avoid deadlocks / overspend races.
        var wallets = await _db.Wallets
            .FromSqlInterpolated($"""
                SELECT wallet_id, customer_id, wallet_name, wallet_type, balance
                FROM wallet
                WHERE customer_id = {customerId}
                  AND wallet_id IN ({request.FromWalletId}, {request.ToWalletId})
                ORDER BY wallet_id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var fromWallet = wallets.FirstOrDefault(w => w.WalletId == request.FromWalletId)
            ?? throw new NotFoundException("From wallet not found.");
        var toWallet = wallets.FirstOrDefault(w => w.WalletId == request.ToWalletId)
            ?? throw new NotFoundException("To wallet not found.");

        if (GetBalance(fromWallet) < request.Amount)
            throw new UnprocessableEntityException("Source wallet has insufficient balance.", "insufficient_balance");

        var now = request.TransferDate ?? DateTime.UtcNow;
        var pairId = Guid.NewGuid();
        var desc = string.IsNullOrWhiteSpace(request.Description)
            ? $"Transfer {fromWallet.WalletName} → {toWallet.WalletName}"
            : request.Description.Trim();

        var outLeg = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = fromWallet.WalletId,
            TransactionType = "transfer_out",
            EntryMethod = "manual",
            TransferPairId = pairId,
            Amount = request.Amount,
            TransactionDate = now,
            Description = desc,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var inLeg = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = toWallet.WalletId,
            TransactionType = "transfer_in",
            EntryMethod = "manual",
            TransferPairId = pairId,
            Amount = request.Amount,
            TransactionDate = now,
            Description = desc,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Transactions.AddRange(outLeg, inLeg);

        fromWallet.Balance = GetBalance(fromWallet) - request.Amount;
        toWallet.Balance = GetBalance(toWallet) + request.Amount;

        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return new TransferResponse { Out = ToResponse(outLeg), In = ToResponse(inLeg) };
    }

    // ── Summary ────────────────────────────────────────────────────────────────────
    public async Task<TransactionSummaryResponse> GetSummaryAsync(
        Guid customerId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
            throw new BadRequestException("month must be between 1 and 12.");

        var startUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddMonths(1);

        // Exclude transfers from all aggregates.
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId
                        && t.TransferPairId == null
                        && t.TransactionType != "transfer_out"
                        && t.TransactionType != "transfer_in"
                        && t.TransactionDate >= startUtc
                        && t.TransactionDate < endUtc)
            .Select(t => new { t.TransactionType, t.Amount, t.CategoryId, t.Merchant, t.TransactionDate })
            .ToListAsync(cancellationToken);

        var income = rows.Where(r => r.TransactionType == "income").Sum(r => r.Amount);
        var expense = rows.Where(r => r.TransactionType == "expense").Sum(r => r.Amount);

        var catNames = await _db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, cancellationToken);

        var byCategory = rows
            .Where(r => r.TransactionType == "expense")
            .GroupBy(r => r.CategoryId)
            .Select(g => new CategorySummaryItem
            {
                CategoryId = g.Key,
                CategoryName = g.Key != null && catNames.TryGetValue(g.Key, out var n) ? n : null,
                Total = g.Sum(x => x.Amount)
            })
            .OrderByDescending(c => c.Total)
            .ToList();

        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(r.TransactionDate!.Value))
            .Select(g => new DaySummaryItem
            {
                Date = g.Key,
                Income = g.Where(x => x.TransactionType == "income").Sum(x => x.Amount),
                Expense = g.Where(x => x.TransactionType == "expense").Sum(x => x.Amount),
                Net = g.Where(x => x.TransactionType == "income").Sum(x => x.Amount)
                      - g.Where(x => x.TransactionType == "expense").Sum(x => x.Amount)
            })
            .OrderBy(d => d.Date)
            .ToList();

        var topMerchants = rows
            .Where(r => r.TransactionType == "expense" && !string.IsNullOrWhiteSpace(r.Merchant))
            .GroupBy(r => r.Merchant!)
            .Select(g => new MerchantSummaryItem { Merchant = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(m => m.Total)
            .Take(10)
            .ToList();

        return new TransactionSummaryResponse
        {
            Income = income,
            Expense = expense,
            Net = income - expense,
            ByCategory = byCategory,
            ByDay = byDay,
            TopMerchants = topMerchants
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────
    private static string NormalizeManualType(string type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant();
        if (!ManualTypes.Contains(t))
            throw new BadRequestException("type must be 'expense' or 'income'. Use POST /transactions/transfer for transfers.");
        return t;
    }

    private static string NormalizeEntryMethod(string? entryMethod)
    {
        if (string.IsNullOrWhiteSpace(entryMethod))
            return "manual";
        var m = entryMethod.Trim().ToLowerInvariant();
        if (!AllowedEntryMethods.Contains(m))
            throw new BadRequestException("entryMethod must be one of: manual, photo, sms_paste, csv_import, sepay_sync.");
        return m;
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BadRequestException("Amount must be greater than zero.");
    }

    /// <summary>A category must exist, be in the customer's expense set (or global income), and its
    /// type must match the transaction type. cat_savings_goal is auto-only (rejected for manual use).</summary>
    private async Task ValidateCategoryForTypeAsync(string? categoryId, string type, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return;

        if (string.Equals(categoryId, GoalFundingCategoryId, StringComparison.Ordinal))
            throw new UnprocessableEntityException(
                "cat_savings_goal is reserved for goal contributions.", "reserved_category");

        var category = await _db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, ct)
            ?? throw new NotFoundException("Category", categoryId);

        if (!string.Equals(category.Type, type, StringComparison.Ordinal))
            throw new UnprocessableEntityException(
                $"Category '{categoryId}' is of type '{category.Type}', which does not match transaction type '{type}'.",
                "category_type_mismatch");
    }

    private async Task<Wallet> LockWalletAsync(Guid customerId, Guid walletId, CancellationToken ct)
    {
        var wallet = (await _db.Wallets
            .FromSqlInterpolated($"""
                SELECT wallet_id, customer_id, wallet_name, wallet_type, balance
                FROM wallet
                WHERE wallet_id = {walletId} AND customer_id = {customerId}
                FOR UPDATE
                """)
            .ToListAsync(ct)).FirstOrDefault();

        return wallet ?? throw new NotFoundException("Wallet", walletId);
    }

    private static decimal GetBalance(Wallet w) => w.Balance ?? 0m;

    /// <summary>income and transfer_in increase balance; expense and transfer_out decrease it.</summary>
    private static decimal SignedDelta(string type, decimal amount)
        => type is "income" or "transfer_in" ? amount : -amount;

    private static TransactionResponse ToResponse(Transaction t)
        => new()
        {
            TransactionId = t.TransactionId,
            CustomerId = t.CustomerId,
            WalletId = t.WalletId,
            CategoryId = t.CategoryId,
            Type = t.TransactionType,
            Amount = t.Amount,
            Description = t.Description,
            Merchant = t.Merchant,
            TransactionDate = t.TransactionDate ?? t.CreatedAt,
            EntryMethod = t.EntryMethod,
            TransferPairId = t.TransferPairId,
            ExternalId = t.ExternalId,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
}
