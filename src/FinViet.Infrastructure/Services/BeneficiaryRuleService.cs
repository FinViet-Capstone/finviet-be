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

    public BeneficiaryRuleService(FinVietDbContext db)
    {
        _db = db;
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
        var originalGuessName = transaction.AiCategoryGuess is null
            ? null
            : await _db.Categories.Where(c => c.CategoryId == transaction.AiCategoryGuess)
                .Select(c => c.CategoryName).FirstOrDefaultAsync(cancellationToken);

        _db.CategoryCorrectionLogs.Add(new CategoryCorrectionLog
        {
            LogId = Guid.NewGuid(),
            CustomerId = customerId,
            TransactionId = transactionId,
            CorrectedCategoryId = request.CategoryId,
            OriginalAiGuess = originalGuessName,
            CreatedAt = DateTime.UtcNow
        });

        transaction.CategoryId = request.CategoryId;
        transaction.IsAiClassified = false;
        transaction.AiConfidence = null;
        transaction.AiClassificationSource = "manual";
        transaction.AiClassifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

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
