using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class BeneficiaryRuleService : IBeneficiaryRuleService
{
    private readonly FinVietDbContext _db;
    private readonly IBudgetService _budgets;

    public BeneficiaryRuleService(FinVietDbContext db, IBudgetService budgets)
    {
        _db = db;
        _budgets = budgets;
    }

    public async Task<CategorizationOutcome> OverrideCategoryAsync(
        Guid customerId, Guid transactionId, OverrideCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .Where(t => t.TransactionId == transactionId)
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { Txn = t, w.CustomerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        if (txn.CustomerId != customerId)
            throw new ForbiddenException("You do not own this transaction.");

        var newCategory = await _db.Categories
            .FirstOrDefaultAsync(
                c => c.CategoryId == request.CategoryId
                     && (!c.CategoryId.StartsWith("custom_")
                         || _db.CustomerCategories.Any(cc =>
                             cc.CustomerId == customerId
                             && cc.CategoryId == c.CategoryId
                             && cc.IsActive)),
                cancellationToken)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var transaction = txn.Txn;
        await ManualCategoryLock.ApplyAsync(
            _db, transaction, customerId, request.CategoryId, alwaysLogCorrection: true, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        if (string.Equals(transaction.TransactionType, "expense", StringComparison.OrdinalIgnoreCase)
            && transaction.TransactionDate is { } date)
            await _budgets.SyncBudgetOnTransactionChangeAsync(customerId, DateOnly.FromDateTime(date), cancellationToken);

        return new CategorizationOutcome
        {
            TransactionId = transactionId,
            CategoryId = request.CategoryId,
            CategoryName = newCategory.CategoryName,
            Confidence = null,
            IsAiClassified = false,
            Queued = false,
            Applied = true,
            Source = "MANUAL"
        };
    }
}
