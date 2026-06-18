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

    public async Task<IReadOnlyList<BeneficiaryRuleResponse>> GetRulesAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _db.BeneficiaryRules
            .Where(r => r.CustomerId == customerId)
            .Join(_db.Categories, r => r.CategoryId, c => c.CategoryId, (r, c) => new BeneficiaryRuleResponse
            {
                RuleId = r.RuleId,
                MatchText = r.MatchText,
                CategoryId = r.CategoryId,
                CategoryName = c.CategoryName,
                IsRecurring = r.IsRecurring
            })
            .OrderBy(r => r.MatchText)
            .ToListAsync(cancellationToken);
    }

    public async Task<BeneficiaryRuleResponse> UpsertRuleAsync(
        Guid customerId, UpsertBeneficiaryRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MatchText))
            throw new BadRequestException("Match text is required.");

        var categoryExists = await _db.Categories.AnyAsync(c => c.CategoryId == request.CategoryId, cancellationToken);
        if (!categoryExists)
            throw new NotFoundException("Category", request.CategoryId);

        var matchText = request.MatchText.Trim();
        var rule = await _db.BeneficiaryRules
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.MatchText == matchText, cancellationToken);

        if (rule is null)
        {
            rule = new BeneficiaryRule
            {
                RuleId = Guid.NewGuid(),
                CustomerId = customerId,
                MatchText = matchText,
                CategoryId = request.CategoryId,
                IsRecurring = request.IsRecurring,
                CreatedAt = DateTime.UtcNow
            };
            _db.BeneficiaryRules.Add(rule);
        }
        else
        {
            rule.CategoryId = request.CategoryId;
            rule.IsRecurring = request.IsRecurring;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await ApplyRuleRetroactivelyAsync(customerId, matchText, request.CategoryId, cancellationToken);

        var categoryName = await _db.Categories
            .Where(c => c.CategoryId == request.CategoryId)
            .Select(c => c.CategoryName)
            .FirstOrDefaultAsync(cancellationToken);

        return new BeneficiaryRuleResponse
        {
            RuleId = rule.RuleId,
            MatchText = rule.MatchText,
            CategoryId = rule.CategoryId,
            CategoryName = categoryName,
            IsRecurring = rule.IsRecurring
        };
    }

    public async Task DeleteRuleAsync(Guid customerId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.BeneficiaryRules
            .FirstOrDefaultAsync(r => r.RuleId == ruleId && r.CustomerId == customerId, cancellationToken)
            ?? throw new NotFoundException("Beneficiary rule", ruleId);

        _db.BeneficiaryRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
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
            .FirstOrDefaultAsync(c => c.CategoryId == request.CategoryId, cancellationToken)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var transaction = txn.Txn;

        // Audit the correction (capture the prior AI guess as human-readable text).
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
        // User-confirmed category is no longer an AI guess.
        transaction.IsAiClassified = false;
        transaction.AiConfidence = null;
        await _db.SaveChangesAsync(cancellationToken);

        if (request.CreateRule && !string.IsNullOrWhiteSpace(transaction.Merchant))
        {
            await UpsertRuleAsync(customerId, new UpsertBeneficiaryRuleRequest
            {
                MatchText = transaction.Merchant!.Trim(),
                CategoryId = request.CategoryId,
                IsRecurring = request.IsRecurring
            }, cancellationToken);
        }

        return new CategorizationOutcome
        {
            TransactionId = transactionId,
            CategoryId = request.CategoryId,
            CategoryName = newCategory.CategoryName,
            Confidence = null,
            IsAiClassified = false,
            Queued = false,
            Source = "RULE"
        };
    }

    /// <summary>Apply a rule to every matching transaction the customer owns (scoped via wallet join).</summary>
    private async Task ApplyRuleRetroactivelyAsync(
        Guid customerId, string matchText, string categoryId, CancellationToken ct)
    {
        var matching = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { Txn = t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.Txn.Merchant != null
                        && EF.Functions.ILike(x.Txn.Merchant, $"%{matchText}%"))
            .Select(x => x.Txn)
            .ToListAsync(ct);

        foreach (var t in matching)
        {
            t.CategoryId = categoryId;
            t.IsAiClassified = false;
            t.AiConfidence = null;
        }

        if (matching.Count > 0)
            await _db.SaveChangesAsync(ct);
    }
}
