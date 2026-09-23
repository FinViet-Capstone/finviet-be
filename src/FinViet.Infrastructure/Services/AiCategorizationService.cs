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
    private const string FeatureClassificationBatch = "classification_batch";
    private const int MaxConcurrentBatchClassifications = 6;
    private const int MaxConcurrentPreviewClassifications = 4;

    // PreviewAsync is called once per HTTP request (photo, and SMS/CSV's per-row path before
    // batching), so unlike PreviewManyAsync's request-scoped gate, this one must be shared across
    // requests/instances of this AddScoped service — a mobile photo batch fires several
    // /extract/photo requests concurrently, and without this gate their simultaneous Gemini calls
    // mostly failed and were silently swallowed by ApplyCategorizationAsync's catch-all, leaving
    // all but one photo in a batch uncategorized.
    private static readonly SemaphoreSlim PreviewGate = new(MaxConcurrentPreviewClassifications);

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
        {
            _logger.LogWarning(
                "Preview categorization skipped for customer {CustomerId}: AI categorization is turned off.",
                customerId);
            return new AiClassificationResult();
        }

        if (!await TryAcquireAsync(customerId, "classification_preview", cancellationToken))
        {
            _logger.LogWarning(
                "Preview categorization rate-limited for customer {CustomerId}.",
                customerId);
            return new AiClassificationResult();
        }

        var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);

        AiClassificationResult result;
        await PreviewGate.WaitAsync(cancellationToken);
        try
        {
            result = await _aiModel.ClassifyAsync(
                input,
                expenseCategories.Keys.ToList(),
                cancellationToken,
                new AiRequestContext("classification_preview", customerId));
        }
        finally
        {
            PreviewGate.Release();
        }

        // The provider only ever returns a name (it doesn't know our ids); resolve it here so
        // callers (SMS/CSV/photo extraction) get a category id they can actually apply, not just
        // a display name.
        if (result.CategoryName is not null
            && expenseCategories.TryGetValue(result.CategoryName, out var categoryId))
        {
            result.CategoryId = categoryId;
        }
        else if (result.CategoryName is not null)
        {
            _logger.LogWarning(
                "Preview categorization for customer {CustomerId} returned category name {CategoryName}, which did not resolve to a known category id.",
                customerId,
                result.CategoryName);
        }

        return result;
    }

    public async Task<IReadOnlyList<AiClassificationResult>> PreviewManyAsync(
        Guid customerId,
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return Array.Empty<AiClassificationResult>();

        var preference = await PreferenceAsync(customerId, cancellationToken);
        if (string.Equals(preference.Mode, ModeOff, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Batch categorization skipped for customer {CustomerId}: AI categorization is turned off ({Count} rows left uncategorized).",
                customerId,
                inputs.Count);
            return inputs.Select(_ => new AiClassificationResult()).ToList();
        }

        var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);
        var categoryNames = expenseCategories.Keys.ToList();

        var results = new AiClassificationResult[inputs.Count];
        using var gate = new SemaphoreSlim(MaxConcurrentBatchClassifications);

        var tasks = inputs.Select(async (input, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                results[index] = await ClassifyOneAsync(customerId, input, expenseCategories, categoryNames, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private async Task<AiClassificationResult> ClassifyOneAsync(
        Guid customerId,
        string input,
        Dictionary<string, string> expenseCategories,
        IReadOnlyList<string> categoryNames,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await TryAcquireAsync(customerId, FeatureClassificationBatch, cancellationToken))
            {
                _logger.LogWarning(
                    "Batch categorization rate-limited for customer {CustomerId}; row left uncategorized.",
                    customerId);
                return new AiClassificationResult();
            }

            var result = await _aiModel.ClassifyAsync(
                input,
                categoryNames,
                cancellationToken,
                new AiRequestContext(FeatureClassificationBatch, customerId));

            if (result.CategoryName is not null
                && expenseCategories.TryGetValue(result.CategoryName, out var categoryId))
            {
                result.CategoryId = categoryId;
            }
            else if (result.CategoryName is not null)
            {
                _logger.LogWarning(
                    "Batch categorization for customer {CustomerId} returned category name {CategoryName}, which did not resolve to a known category id; row left uncategorized.",
                    customerId,
                    result.CategoryName);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch category preview failed for one row; leaving it uncategorized.");
            return new AiClassificationResult();
        }
    }

    public async Task<IReadOnlyList<CategorizationOutcome>> CategorizeManyAsync(
        Guid customerId,
        IReadOnlyList<Guid> transactionIds,
        CancellationToken cancellationToken = default)
    {
        if (transactionIds.Count == 0)
            return Array.Empty<CategorizationOutcome>();

        var transactions = await _db.Transactions
            .Where(t => t.CustomerId == customerId && transactionIds.Contains(t.TransactionId))
            .ToListAsync(cancellationToken);
        var outcomes = new List<CategorizationOutcome>(transactions.Count);
        var candidates = new List<Transaction>();

        foreach (var txn in transactions)
        {
            if (string.Equals(txn.AiClassificationSource, SourceManual, StringComparison.Ordinal))
            {
                outcomes.Add(Outcome(txn, txn.CategoryId, null, applied: false, source: "MANUAL", reason: "manual_locked"));
                continue;
            }

            if (txn.CategoryId is not null)
            {
                outcomes.Add(Outcome(txn, txn.CategoryId, null, applied: false, source: "SKIPPED", reason: "already_categorized"));
                continue;
            }

            var rule = await _ruleService.ResolveAsync(customerId, txn.Merchant, txn.Description, cancellationToken);
            if (rule is not null && await IsVisibleCategoryAsync(customerId, rule.CategoryId, cancellationToken))
            {
                txn.CategoryId = rule.CategoryId;
                txn.IsAiClassified = false;
                txn.AiConfidence = null;
                txn.AiCategoryGuess = null;
                txn.AiClassificationSource = SourceRule;
                txn.AiClassifiedAt = DateTime.UtcNow;
                await _ruleService.IncrementAppliedAsync(rule.RuleId, cancellationToken: cancellationToken);
                outcomes.Add(Outcome(txn, rule.CategoryId, rule.CategoryName, applied: true, source: "RULE"));
                continue;
            }

            candidates.Add(txn);
        }

        var preference = await PreferenceAsync(customerId, cancellationToken);
        var expenseCategories = await ExpenseCategoriesAsync(customerId, cancellationToken);
        var categoryNames = expenseCategories.Keys.ToList();
        var modeOff = string.Equals(preference.Mode, ModeOff, StringComparison.Ordinal);

        var classifications = new BatchClassification[candidates.Count];
        using (var gate = new SemaphoreSlim(MaxConcurrentBatchClassifications))
        {
            await Task.WhenAll(candidates.Select(async (txn, index) =>
            {
                var input = BuildInput(txn);
                if (modeOff)
                {
                    classifications[index] = BatchClassification.Failed("mode_off");
                    return;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    classifications[index] = BatchClassification.Failed("empty_input");
                    return;
                }

                await gate.WaitAsync(cancellationToken);
                try
                {
                    classifications[index] = await ClassifyBatchRowAsync(
                        customerId, input, expenseCategories, categoryNames, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        // The Gemini calls can take a while; a row the user accepted or dismissed in the meantime
        // must keep its manual state, so re-read the lock right before persisting.
        var candidateIds = candidates.Select(c => c.TransactionId).ToList();
        var lockedIds = (await _db.Transactions.AsNoTracking()
                .Where(t => candidateIds.Contains(t.TransactionId)
                            && (t.AiClassificationSource == SourceManual || t.CategoryId != null))
                .Select(t => t.TransactionId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        for (var i = 0; i < candidates.Count; i++)
        {
            if (lockedIds.Contains(candidates[i].TransactionId))
            {
                await _db.Entry(candidates[i]).ReloadAsync(cancellationToken);
                outcomes.Add(Outcome(candidates[i], candidates[i].CategoryId, null, applied: false, source: "SKIPPED", reason: "changed_while_queued"));
                continue;
            }

            outcomes.Add(ApplyBatchClassification(candidates[i], classifications[i], preference));
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var outcome in outcomes.Where(o => o.Source is not ("MANUAL" or "SKIPPED")))
        {
            await RecordDecisionAsync(
                customerId,
                outcome.TransactionId,
                outcome.Source.ToLowerInvariant(),
                outcome.Confidence,
                outcome.Applied,
                outcome.Reason,
                cancellationToken);
        }

        return outcomes;
    }

    private static CategorizationOutcome ApplyBatchClassification(
        Transaction txn,
        BatchClassification classification,
        (string Mode, decimal Threshold) preference)
    {
        txn.AiClassifiedAt = DateTime.UtcNow;

        if (classification.FailureReason is not null)
        {
            txn.IsAiClassified = false;
            txn.AiConfidence = null;
            txn.AiCategoryGuess = null;
            txn.AiClassificationSource = SourceFallback;
            return Outcome(txn, txn.CategoryId, null, applied: false, source: "FALLBACK", reason: classification.FailureReason);
        }

        txn.IsAiClassified = false;
        txn.AiClassificationSource = SourceSuggestion;

        if (classification.CategoryId is null)
        {
            // The model answered but not with a category we can use: "unsure", not "failed".
            txn.AiConfidence = null;
            txn.AiCategoryGuess = null;
            return Outcome(txn, txn.CategoryId, null, applied: false, source: "AI_SUGGESTION", reason: "unresolved_category");
        }

        txn.AiConfidence = classification.Confidence;
        txn.AiCategoryGuess = classification.CategoryId;

        if (string.Equals(preference.Mode, ModeAuto, StringComparison.Ordinal)
            && classification.Confidence >= preference.Threshold)
        {
            txn.CategoryId = classification.CategoryId;
            txn.IsAiClassified = true;
            txn.AiClassificationSource = SourceAuto;
            return Outcome(txn, classification.CategoryId, classification.CategoryName, applied: true, source: "AI_AUTO");
        }

        var reason = string.Equals(preference.Mode, ModeSuggestOnly, StringComparison.Ordinal)
            ? "suggest_only"
            : "below_threshold";
        return Outcome(
            txn,
            txn.CategoryId,
            null,
            applied: false,
            source: "AI_SUGGESTION",
            suggestedCategoryId: classification.CategoryId,
            suggestedCategoryName: classification.CategoryName,
            reason: reason);
    }

    private async Task<BatchClassification> ClassifyBatchRowAsync(
        Guid customerId,
        string input,
        Dictionary<string, string> expenseCategories,
        IReadOnlyList<string> categoryNames,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await TryAcquireAsync(customerId, FeatureClassificationBatch, cancellationToken))
                return BatchClassification.Failed("rate_limited");

            var result = await _aiModel.ClassifyAsync(
                input,
                categoryNames,
                cancellationToken,
                new AiRequestContext(FeatureClassificationBatch, customerId));

            return result.CategoryName is not null
                   && expenseCategories.TryGetValue(result.CategoryName, out var categoryId)
                ? new BatchClassification(null, categoryId, result.CategoryName, result.Confidence)
                : new BatchClassification(null, null, null, 0m);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background categorization failed for one row of customer {CustomerId}.", customerId);
            return BatchClassification.Failed("provider_unavailable");
        }
    }

    private sealed record BatchClassification(
        string? FailureReason,
        string? CategoryId,
        string? CategoryName,
        decimal Confidence)
    {
        public static BatchClassification Failed(string reason) => new(reason, null, null, 0m);
    }

    private static string BuildInput(Transaction txn)
        => !string.IsNullOrWhiteSpace(txn.Merchant) ? txn.Merchant.Trim()
            : (txn.Description ?? string.Empty).Trim();

    private async Task<Dictionary<string, string>> ExpenseCategoriesAsync(Guid customerId, CancellationToken ct)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Type == "expense"
                        && c.CategoryName != UncategorizedName
                        && c.CategoryId != "cat_savings_goal"
                        && (!c.CategoryId.StartsWith("custom_")
                            || _db.CustomerCategories.Any(cc =>
                                cc.CustomerId == customerId
                                && cc.CategoryId == c.CategoryId
                                && cc.IsActive)))
            .Select(c => new { c.CategoryName, c.CategoryId })
            .ToListAsync(ct);

        // Dedupe on a case-insensitive key before building the dictionary — a custom category
        // sharing a name (any casing) with another one would otherwise throw on Add and silently
        // disable categorization for the whole batch (caught upstream as one opaque warning).
        // First match wins; matches the case-insensitive comparer used for the name→id lookup.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in categories)
        {
            if (!result.ContainsKey(c.CategoryName))
                result[c.CategoryName] = c.CategoryId;
            else
                _logger.LogWarning(
                    "Duplicate expense category name {CategoryName} for customer {CustomerId} (ids {ExistingId} vs {DuplicateId}); keeping the first.",
                    c.CategoryName,
                    customerId,
                    result[c.CategoryName],
                    c.CategoryId);
        }

        return result;
    }

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
