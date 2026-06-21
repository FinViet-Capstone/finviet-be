using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
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
    private readonly ILogger<AiCategorizationService> _logger;

    public AiCategorizationService(
        FinVietDbContext db,
        IGeminiClient gemini,
        ILogger<AiCategorizationService> logger)
    {
        _db = db;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<CategorizationOutcome> CategorizeTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        var input = BuildInput(txn);
        if (string.IsNullOrWhiteSpace(input))
        {
            await ApplyUncategorizedAsync(txn, cancellationToken);
            return Outcome(txn, null, null, isAi: false, queued: false, "FALLBACK");
        }

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

            await ApplyUncategorizedAsync(txn, cancellationToken);
            return Outcome(txn, txn.CategoryId, UncategorizedName, isAi: false, queued: false, "FALLBACK");
        }
        catch (GeminiUnavailableException ex)
        {
            _logger.LogWarning(ex, "Gemini unavailable; falling back to Uncategorized for txn {Id}.", transactionId);
            await ApplyUncategorizedAsync(txn, cancellationToken);
            return Outcome(txn, txn.CategoryId, UncategorizedName, isAi: false, queued: false, "FALLBACK");
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
            return true;

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
            return true;
        }
        catch (GeminiUnavailableException)
        {
            return false;
        }
    }

    private static string BuildInput(Transaction txn)
        => !string.IsNullOrWhiteSpace(txn.Merchant) ? txn.Merchant!.Trim()
            : (txn.Description ?? string.Empty).Trim();

    private async Task<Dictionary<string, string>> ExpenseCategoriesAsync(CancellationToken ct)
        => await _db.Categories
            .Where(c => c.Type == CategoryType.Expense && c.CategoryName != UncategorizedName && c.CategoryId != "cat_savings_goal")
            .ToDictionaryAsync(c => c.CategoryName, c => c.CategoryId, ct);

    private async Task ApplyUncategorizedAsync(Transaction txn, CancellationToken ct)
    {
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
