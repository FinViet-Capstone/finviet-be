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

    private const string ModeOff = "off";
    private const string ModeSuggestOnly = "suggest_only";
    private const string ModeAuto = "high_confidence_auto";
    private const string SourceManual = "manual";
    private const string SourceRule = "merchant_rule";
    private const string SourceAuto = "ai_auto";
    private const string SourceSuggestion = "ai_suggestion";
    private const string SourceFallback = "fallback";

    private readonly FinVietDbContext _db;
    private readonly IAiModelClient _aiModel;
    private readonly IMerchantRuleService _ruleService;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly IAiTelemetryRecorder _telemetry;
    private readonly ILogger<AiCategorizationService> _logger;

    public AiCategorizationService(
        FinVietDbContext db,
        IAiModelClient aiModel,
        IMerchantRuleService ruleService,
        IAiRateLimiter rateLimiter,
        IAiTelemetryRecorder telemetry,
        ILogger<AiCategorizationService> logger)
    {
        _db = db;
        _aiModel = aiModel;
        _ruleService = ruleService;
        _rateLimiter = rateLimiter;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CategorizationOutcome> CategorizeTransactionAsync(
        Guid customerId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(
                t => t.TransactionId == transactionId && t.CustomerId == customerId,
                cancellationToken)
            ?? throw new NotFoundException("Transaction", transactionId);

        if (string.Equals(txn.AiClassificationSource, SourceManual, StringComparison.Ordinal))
        {
            await RecordDecisionAsync(
                customerId,
                transactionId,
                SourceManual,
                txn.AiConfidence,
                applied: false,
                reason: "manual_locked",
                cancellationToken);
            return Outcome(
                txn,
                txn.CategoryId,
                await CategoryNameAsync(txn.CategoryId, cancellationToken),
                applied: false,
                source: "MANUAL",
                reason: "manual_locked");
        }

        var rule = await _ruleService.ResolveAsync(
            customerId,
            txn.Merchant,
            txn.Description,
            cancellationToken);
        if (rule is not null && await IsVisibleCategoryAsync(customerId, rule.CategoryId, cancellationToken))
        {
            txn.CategoryId = rule.CategoryId;
            txn.IsAiClassified = false;
            txn.AiConfidence = null;
            txn.AiCategoryGuess = null;
            txn.AiClassificationSource = SourceRule;
            txn.AiClassifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _ruleService.IncrementAppliedAsync(rule.RuleId, cancellationToken: cancellationToken);
            await RecordDecisionAsync(
                customerId,
                transactionId,
                SourceRule,
                confidence: null,
                applied: true,
                reason: null,
                cancellationToken);

            return Outcome(txn, rule.CategoryId, rule.CategoryName, applied: true, source: "RULE");
        }

        var preference = await PreferenceAsync(customerId, cancellationToken);
        if (string.Equals(preference.Mode, ModeOff, StringComparison.Ordinal))
        {
            txn.AiClassificationSource = null;
            txn.AiConfidence = null;
            txn.AiCategoryGuess = null;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordDecisionAsync(
                customerId,
                transactionId,
                source: "off",
                confidence: null,
                applied: false,
                reason: "mode_off",
                cancellationToken);
            return Outcome(
                txn,
                txn.CategoryId,
                await CategoryNameAsync(txn.CategoryId, cancellationToken),
                applied: false,
                source: "OFF",
                reason: "mode_off");
        }

        var input = BuildInput(txn);
        if (string.IsNullOrWhiteSpace(input))
            return await ApplyFallbackAsync(customerId, txn, "empty_input", cancellationToken);

        if (!await TryAcquireAsync(customerId, "classification", cancellationToken))
            return await ApplyFallbackAsync(customerId, txn, "rate_limited", cancellationToken);

        var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);
        try
        {
            var result = await _aiModel.ClassifyAsync(
                input,
                expenseCategories.Keys.ToList(),
                cancellationToken,
                new AiRequestContext("classification", customerId));
            if (result.CategoryName is null ||
                !expenseCategories.TryGetValue(result.CategoryName, out var categoryId))
            {
                return await ApplyFallbackAsync(customerId, txn, "unresolved_category", cancellationToken);
            }

            txn.AiConfidence = result.Confidence;
            txn.AiCategoryGuess = categoryId;
            txn.AiClassifiedAt = DateTime.UtcNow;

            var shouldApply = string.Equals(preference.Mode, ModeAuto, StringComparison.Ordinal)
                              && result.Confidence >= preference.Threshold;
            if (shouldApply)
            {
                txn.CategoryId = categoryId;
                txn.IsAiClassified = true;
                txn.AiClassificationSource = SourceAuto;
                await _db.SaveChangesAsync(cancellationToken);
                await RecordDecisionAsync(
                    customerId,
                    transactionId,
                    SourceAuto,
                    result.Confidence,
                    applied: true,
                    reason: null,
                    cancellationToken);
                return Outcome(txn, categoryId, result.CategoryName, applied: true, source: "AI_AUTO");
            }

            txn.IsAiClassified = false;
            txn.AiClassificationSource = SourceSuggestion;
            await _db.SaveChangesAsync(cancellationToken);
            var suggestionReason = string.Equals(preference.Mode, ModeSuggestOnly, StringComparison.Ordinal)
                ? "suggest_only"
                : "below_threshold";
            await RecordDecisionAsync(
                customerId,
                transactionId,
                SourceSuggestion,
                result.Confidence,
                applied: false,
                suggestionReason,
                cancellationToken);
            return Outcome(
                txn,
                txn.CategoryId,
                await CategoryNameAsync(txn.CategoryId, cancellationToken),
                applied: false,
                source: "AI_SUGGESTION",
                suggestedCategoryId: categoryId,
                suggestedCategoryName: result.CategoryName,
                reason: suggestionReason);
        }
        catch (AiProviderUnavailableException ex)
        {
            _logger.LogWarning(
                ex,
                "AI provider unavailable during categorization for transaction {TransactionId}.",
                transactionId);
            return await ApplyFallbackAsync(customerId, txn, "provider_unavailable", cancellationToken);
        }
    }

    public async Task<AiClassificationResult> PreviewAsync(
        Guid customerId,
        string input,
        CancellationToken cancellationToken = default)
    {
        var preference = await PreferenceAsync(customerId, cancellationToken);
        if (string.Equals(preference.Mode, ModeOff, StringComparison.Ordinal))
            return new AiClassificationResult();

        if (!await TryAcquireAsync(customerId, "classification_preview", cancellationToken))
            return new AiClassificationResult();

        var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);
        return await _aiModel.ClassifyAsync(
            input,
            expenseCategories.Keys.ToList(),
            cancellationToken,
            new AiRequestContext("classification_preview", customerId));
    }

    public async Task<bool> ReprocessAsync(
        Guid customerId,
        Guid transactionId,
        string rawInput,
        CancellationToken cancellationToken = default)
    {
        var txn = await _db.Transactions
            .FirstOrDefaultAsync(
                t => t.TransactionId == transactionId && t.CustomerId == customerId,
                cancellationToken);
        if (txn is null || string.Equals(txn.AiClassificationSource, SourceManual, StringComparison.Ordinal))
            return true;

        try
        {
            if (!await TryAcquireAsync(customerId, "classification_reprocess", cancellationToken))
                return false;

            var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);
            var result = await _aiModel.ClassifyAsync(
                rawInput,
                expenseCategories.Keys.ToList(),
                cancellationToken,
                new AiRequestContext("classification_reprocess", customerId));
            if (result.CategoryName is not null && expenseCategories.TryGetValue(result.CategoryName, out var categoryId))
            {
                txn.AiConfidence = result.Confidence;
                txn.AiCategoryGuess = categoryId;
                txn.AiClassificationSource = SourceSuggestion;
                txn.AiClassifiedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                await RecordDecisionAsync(
                    customerId,
                    transactionId,
                    SourceSuggestion,
                    result.Confidence,
                    applied: false,
                    reason: "reprocess_suggestion",
                    cancellationToken);
            }
            else
            {
                await RecordDecisionAsync(
                    customerId,
                    transactionId,
                    SourceFallback,
                    result.Confidence,
                    applied: false,
                    reason: "reprocess_unresolved",
                    cancellationToken);
            }
            return true;
        }
        catch (AiProviderUnavailableException)
        {
            return false;
        }
    }

    private static string BuildInput(Transaction txn)
        => !string.IsNullOrWhiteSpace(txn.Merchant) ? txn.Merchant.Trim()
            : (txn.Description ?? string.Empty).Trim();

    private async Task<Dictionary<string, string>> ExpenseCategoriesAsync(Guid customerId, CancellationToken ct)
        => await _db.Categories
            .AsNoTracking()
            .Where(c => c.Type == "expense"
                        && c.CategoryName != UncategorizedName
                        && c.CategoryId != "cat_savings_goal"
                        && (!c.CategoryId.StartsWith("custom_")
                            || _db.CustomerCategories.Any(cc =>
                                cc.CustomerId == customerId
                                && cc.CategoryId == c.CategoryId
                                && cc.IsActive)))
            .ToDictionaryAsync(c => c.CategoryName, c => c.CategoryId, ct);

    private async Task<bool> IsVisibleCategoryAsync(Guid customerId, string categoryId, CancellationToken ct)
    {
        if (!categoryId.StartsWith("custom_", StringComparison.Ordinal))
            return true;

        return await _db.CustomerCategories.AsNoTracking().AnyAsync(
            cc => cc.CustomerId == customerId && cc.CategoryId == categoryId && cc.IsActive,
            ct);
    }

    private async Task<string?> CategoryNameAsync(string? categoryId, CancellationToken ct)
    {
        if (categoryId is null)
            return null;

        return await _db.Categories.AsNoTracking()
            .Where(c => c.CategoryId == categoryId)
            .Select(c => c.CategoryName)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<(string Mode, decimal Threshold)> PreferenceAsync(Guid customerId, CancellationToken ct)
    {
        var row = await _db.AiCustomerPreferences.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => new { p.CategorizationMode, p.AutoCategorizationThreshold })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? (ModeSuggestOnly, 0.85m)
            : (row.CategorizationMode, row.AutoCategorizationThreshold);
    }

    private async Task<CategorizationOutcome> ApplyFallbackAsync(
        Guid customerId,
        Transaction txn,
        string reason,
        CancellationToken ct)
    {
        txn.IsAiClassified = false;
        txn.AiConfidence = null;
        txn.AiCategoryGuess = null;
        txn.AiClassificationSource = SourceFallback;
        txn.AiClassifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await RecordDecisionAsync(
            customerId,
            txn.TransactionId,
            SourceFallback,
            confidence: null,
            applied: false,
            reason,
            ct);
        return Outcome(
            txn,
            txn.CategoryId,
            await CategoryNameAsync(txn.CategoryId, ct),
            applied: false,
            source: "FALLBACK",
            reason: reason);
    }

    private async Task<bool> TryAcquireAsync(
        Guid customerId,
        string feature,
        CancellationToken cancellationToken)
    {
        if (await _rateLimiter.TryAcquireAsync(customerId, feature, cancellationToken))
            return true;

        await _telemetry.RecordUsageAsync(
            new AiUsageRecord(
                feature,
                "gemini",
                "rate_limited",
                customerId),
            cancellationToken);
        return false;
    }

    private Task RecordDecisionAsync(
        Guid customerId,
        Guid transactionId,
        string source,
        decimal? confidence,
        bool applied,
        string? reason,
        CancellationToken cancellationToken)
        => _telemetry.RecordAuditAsync(
            new AiAuditRecord(
                "categorization_decision",
                "system",
                customerId,
                CorrelationId: transactionId,
                Metadata: new Dictionary<string, object?>
                {
                    ["source"] = source,
                    ["confidence"] = confidence,
                    ["applied"] = applied,
                    ["reason"] = reason
                }),
            cancellationToken);

    private static CategorizationOutcome Outcome(
        Transaction txn,
        string? categoryId,
        string? categoryName,
        bool applied,
        string source,
        string? suggestedCategoryId = null,
        string? suggestedCategoryName = null,
        string? reason = null)
        => new()
        {
            TransactionId = txn.TransactionId,
            CategoryId = categoryId,
            CategoryName = categoryName,
            Confidence = txn.AiConfidence,
            IsAiClassified = string.Equals(source, "AI_AUTO", StringComparison.Ordinal),
            Queued = false,
            Applied = applied,
            SuggestedCategoryId = suggestedCategoryId,
            SuggestedCategoryName = suggestedCategoryName,
            Reason = reason,
            Source = source
        };
}
