using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;

namespace FinViet.Infrastructure.Services;

public class AiCategorizationService : IAiCategorizationService
{
    public const string UncategorizedName = "Chưa phân loại";

    private readonly FinVietDbContext _db;
    private readonly IGeminiClient _gemini;
    private readonly IAiClassificationQueue _queue;
    private readonly ILogger<AiCategorizationService> _logger;

    public AiCategorizationService(
        FinVietDbContext db,
        IGeminiClient gemini,
        IAiClassificationQueue queue,
        ILogger<AiCategorizationService> logger)
    {
        _db = db;
        _gemini = gemini;
        _queue = queue;
        _logger = logger;
    }

    public async Task<CategorizationOutcome> CategorizeTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        var customerId = await _db.Wallets
            .Where(w => w.WalletId == txn.WalletId)
            .Select(w => w.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        var input = BuildInput(txn);
        if (string.IsNullOrWhiteSpace(input))
        {
            await ApplyUncategorizedAsync(txn, queued: false, cancellationToken);
            return Outcome(txn, null, null, isAi: false, queued: false, "FALLBACK");
        }

        // 1. Beneficiary rule (deterministic, retroactive).
        if (customerId.HasValue)
        {
            var ruleCategoryId = await MatchRuleAsync(customerId.Value, input, cancellationToken);
            if (ruleCategoryId is not null)
            {
                txn.CategoryId = ruleCategoryId;
                txn.IsAiClassified = false;
                txn.AiConfidence = null;
                txn.AiCategoryGuess = ruleCategoryId;
                await _db.SaveChangesAsync(cancellationToken);

                var ruleName = await CategoryNameAsync(ruleCategoryId, cancellationToken);
                return Outcome(txn, ruleCategoryId, ruleName, isAi: false, queued: false, "RULE");
            }
        }

        // 2. Gemini classification.
        var expenseCategories = await ExpenseCategoriesAsync(cancellationToken);
        try
        {
            var result = await _gemini.ClassifyAsync(
                input,
                expenseCategories.Keys.ToList(),
                cancellationToken);

            if (result.CategoryName is not null && expenseCategories.TryGetValue(result.CategoryName, out var catId))
            {
                txn.CategoryId = catId;
                txn.IsAiClassified = true;
                txn.AiConfidence = result.Confidence;
                txn.AiCategoryGuess = catId;
                await _db.SaveChangesAsync(cancellationToken);
                return Outcome(txn, catId, result.CategoryName, isAi: true, queued: false, "AI");
            }

            // Resolved to nothing usable → treat as uncategorized (not a service outage, no re-queue).
            await ApplyUncategorizedAsync(txn, queued: false, cancellationToken);
            return Outcome(txn, txn.CategoryId, UncategorizedName, isAi: false, queued: false, "FALLBACK");
        }
        catch (GeminiUnavailableException ex)
        {
            _logger.LogWarning(ex, "Gemini unavailable; falling back to Uncategorized + queue for txn {Id}.", transactionId);
            await ApplyUncategorizedAsync(txn, queued: true, cancellationToken);
            if (customerId.HasValue)
                await _queue.EnqueueAsync(transactionId, customerId.Value, input, cancellationToken);
            return Outcome(txn, txn.CategoryId, UncategorizedName, isAi: false, queued: true, "FALLBACK");
        }
    }

    public async Task<AiClassificationResult> PreviewAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var expenseCategories = await ExpenseCategoriesAsync(cancellationToken);
        return await _gemini.ClassifyAsync(input, expenseCategories.Keys.ToList(), cancellationToken);
    }

    public async Task<bool> ReprocessAsync(
        Guid transactionId,
        string rawInput,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);
        if (txn is null)
            return true; // transaction gone; nothing to do — let the queue mark it done.

        var expenseCategories = await ExpenseCategoriesAsync(cancellationToken);
        try
        {
            var result = await _gemini.ClassifyAsync(rawInput, expenseCategories.Keys.ToList(), cancellationToken);
            if (result.CategoryName is not null && expenseCategories.TryGetValue(result.CategoryName, out var catId))
            {
                txn.CategoryId = catId;
                txn.IsAiClassified = true;
                txn.AiConfidence = result.Confidence;
                txn.AiCategoryGuess = catId;
                await _db.SaveChangesAsync(cancellationToken);
            }
            // Even an unresolved-but-successful call counts as processed (Gemini is up).
            return true;
        }
        catch (GeminiUnavailableException)
        {
            return false; // still down — caller will retry later.
        }
    }

    private static string BuildInput(Transaction txn)
        => !string.IsNullOrWhiteSpace(txn.Merchant) ? txn.Merchant!.Trim()
            : (txn.Description ?? string.Empty).Trim();

    private async Task<string?> MatchRuleAsync(Guid customerId, string input, CancellationToken ct)
    {
        var rules = await _db.BeneficiaryRules
            .Where(r => r.CustomerId == customerId)
            .Select(r => new { r.MatchText, r.CategoryId })
            .ToListAsync(ct);

        var match = rules.FirstOrDefault(r =>
            input.Contains(r.MatchText, StringComparison.OrdinalIgnoreCase) ||
            r.MatchText.Contains(input, StringComparison.OrdinalIgnoreCase));

        return match?.CategoryId;
    }

    private async Task<Dictionary<string, string>> ExpenseCategoriesAsync(CancellationToken ct)
    {
        // Closed set: expense categories excluding the auto-only goal-funding slug.
        return await _db.Categories
            .Where(c => c.Type == "expense" && c.CategoryId != "cat_savings_goal")
            .ToDictionaryAsync(c => c.CategoryName, c => c.CategoryId, ct);
    }

    private async Task<string?> CategoryNameAsync(string categoryId, CancellationToken ct)
        => await _db.Categories.Where(c => c.CategoryId == categoryId)
            .Select(c => c.CategoryName).FirstOrDefaultAsync(ct);

    private async Task ApplyUncategorizedAsync(Transaction txn, bool queued, CancellationToken ct)
    {
        // Uncategorized transactions carry category_id = NULL (no placeholder slug).
        txn.CategoryId = null;
        txn.IsAiClassified = false;
        txn.AiConfidence = null;
        txn.AiCategoryGuess = null;
        await _db.SaveChangesAsync(ct);
    }

    private static CategorizationOutcome Outcome(
        Transaction txn, string? catId, string? catName, bool isAi, bool queued, string source)
        => new()
        {
            TransactionId = txn.TransactionId,
            CategoryId = catId,
            CategoryName = catName,
            Confidence = txn.AiConfidence,
            IsAiClassified = isAi,
            Queued = queued,
            Source = source
        };
}
