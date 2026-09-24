using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.Ai.Commands.OverrideCategoryBatch;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Ai.Commands.OverrideCategoryBatch;

/// <summary>
/// Applies each item independently with the same effects as the single override route. Only
/// expenses are eligible (income is never categorized). A transaction that is missing or not
/// the caller's is reported as <c>not_found</c> so ids of other customers are not disclosed.
/// Budgets are re-checked once per affected month, not per row.
/// </summary>
public class OverrideCategoryBatchCommandHandler
    : IRequestHandler<OverrideCategoryBatchCommand, OverrideCategoryBatchResponse>
{
    private readonly FinVietDbContext _db;
    private readonly IBudgetService _budgets;

    public OverrideCategoryBatchCommandHandler(FinVietDbContext db, IBudgetService budgets)
    {
        _db = db;
        _budgets = budgets;
    }

    public async Task<OverrideCategoryBatchResponse> Handle(
        OverrideCategoryBatchCommand command, CancellationToken cancellationToken)
    {
        var customerId = command.CustomerId;
        var items = command.Request.Items;

        var transactionIds = items.Select(i => i.TransactionId).ToList();
        var transactions = await _db.Transactions
            .Where(t => transactionIds.Contains(t.TransactionId))
            .Join(_db.Wallets.Where(w => w.CustomerId == customerId), t => t.WalletId, w => w.WalletId, (t, _) => t)
            .ToDictionaryAsync(t => t.TransactionId, cancellationToken);

        var categoryIds = items.Select(i => i.CategoryId).Distinct().ToList();
        var availableCategories = await _db.Categories
            .Where(c => categoryIds.Contains(c.CategoryId)
                        && (!c.CategoryId.StartsWith("custom_")
                            || _db.CustomerCategories.Any(cc =>
                                cc.CustomerId == customerId
                                && cc.CategoryId == c.CategoryId
                                && cc.IsActive)))
            .Select(c => c.CategoryId)
            .ToListAsync(cancellationToken);
        var available = availableCategories.ToHashSet();

        var results = new List<OverrideCategoryBatchItemResult>(items.Count);
        var affectedMonths = new HashSet<DateOnly>();

        foreach (var item in items)
        {
            string outcome;
            if (!transactions.TryGetValue(item.TransactionId, out var transaction))
                outcome = OverrideBatchOutcomes.NotFound;
            else if (!available.Contains(item.CategoryId))
                outcome = OverrideBatchOutcomes.CategoryUnavailable;
            else if (!string.Equals(transaction.TransactionType, "expense", StringComparison.OrdinalIgnoreCase))
                outcome = OverrideBatchOutcomes.NotEligible;
            else
            {
                await ManualCategoryLock.ApplyAsync(
                    _db, transaction, customerId, item.CategoryId, alwaysLogCorrection: true, cancellationToken);
                if (transaction.TransactionDate is { } date)
                    affectedMonths.Add(new DateOnly(date.Year, date.Month, 1));
                outcome = OverrideBatchOutcomes.Ok;
            }

            results.Add(new OverrideCategoryBatchItemResult(item.TransactionId, outcome));
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var month in affectedMonths)
            await _budgets.SyncBudgetOnTransactionChangeAsync(customerId, month, cancellationToken);

        return new OverrideCategoryBatchResponse(results);
    }
}
