using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>Parses SMS/CSV into candidate rows, then asks the AI categorizer for a category
/// suggestion per row. AI failures degrade gracefully to a null suggestion (row still returned).</summary>
public class TransactionExtractService : ITransactionExtractService
{
    private readonly ISmsTransactionParser _smsParser;
    private readonly IBankStatementParser _bankParser;
    private readonly IAiCategorizationService _categorization;
    private readonly IMerchantRuleService _ruleService;
    private readonly ILogger<TransactionExtractService> _logger;

    public TransactionExtractService(
        ISmsTransactionParser smsParser,
        IBankStatementParser bankParser,
        IAiCategorizationService categorization,
        IMerchantRuleService ruleService,
        ILogger<TransactionExtractService> logger)
    {
        _smsParser = smsParser;
        _bankParser = bankParser;
        _categorization = categorization;
        _ruleService = ruleService;
        _logger = logger;
    }

    public async Task<ExtractResponse> ExtractSmsAsync(Guid customerId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new Application.Common.Exceptions.BadRequestException(
                "Vui lòng dán nội dung tin nhắn biến động số dư từ ngân hàng. " +
                "Mỗi tin cần có số tiền kèm đơn vị (VND/VNĐ/đ), ví dụ: \"-350,000 VND\". " +
                "Có thể dán nhiều tin, mỗi tin cách nhau một dòng trống.");

        var parsed = _smsParser.Parse(text);
        return await BuildResponseAsync(customerId, parsed, cancellationToken);
    }

    public async Task<ExtractResponse> ExtractCsvAsync(Guid customerId, Stream fileStream, string fileExtension, int? maxRows, CancellationToken cancellationToken = default)
    {
        var parsed = _bankParser.Parse(fileStream, fileExtension, maxRows);
        return await BuildResponseAsync(customerId, parsed, cancellationToken);
    }

    private async Task<ExtractResponse> BuildResponseAsync(Guid customerId, ParseResult parsed, CancellationToken ct)
    {
        var response = new ExtractResponse
        {
            TotalScanned = parsed.TotalRowsScanned,
            Skipped = parsed.SkippedDuringParse,
            Errors = new List<string>(parsed.ParseErrors)
        };

        // Load the customer's rules once (already ordered longest-keyword-first). A matching rule
        // takes precedence over AI categorization for that row (BUSINESS_LOGIC §2b).
        var rules = await _ruleService.GetRulesAsync(customerId, ct);

        // Rows a rule couldn't resolve are classified together via PreviewManyAsync instead of one
        // at a time — a large statement import can have well over a hundred rows, and classifying
        // them sequentially (one AI round trip each) made big imports painfully slow. Rule matching
        // itself stays a synchronous, in-memory per-row pass since it's cheap.
        var items = new List<ExtractedTransactionItem>(parsed.Rows.Count);
        var pendingAiIndexes = new List<int>();
        var pendingAiInputs = new List<string>();

        foreach (var row in parsed.Rows)
        {
            var item = new ExtractedTransactionItem
            {
                Amount = row.Amount,
                Type = row.TransactionType,
                Description = row.Note,
                // Prefer the actual counterparty/beneficiary name when the source had a distinct
                // column for it; otherwise fall back to the transaction description, same as before
                // CorrespondentName existed.
                Merchant = string.IsNullOrWhiteSpace(row.CorrespondentName) ? row.Note : row.CorrespondentName,
                TransactionDate = row.TransactionDate
            };
            items.Add(item);

            // Rule/AI matching should still see the correspondent name alongside the description —
            // e.g. a generic "Chuyen khoan" description with correspondent "Circle K" needs both
            // texts to categorize correctly — even though the two are now kept as separate display
            // fields on the item above instead of pre-merged into Note.
            var categorizationInput = string.IsNullOrWhiteSpace(row.CorrespondentName)
                ? row.Note
                : $"{row.Note} {row.CorrespondentName}";

            if (!string.Equals(item.Type, "EXPENSE", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(categorizationInput))
            {
                continue;
            }

            var ruleMatch = rules.FirstOrDefault(r =>
                categorizationInput.Contains(r.MerchantKeyword, StringComparison.OrdinalIgnoreCase));
            if (ruleMatch is not null)
            {
                item.CategoryId = ruleMatch.CategoryId;
                item.CategoryName = ruleMatch.CategoryName;
                item.Confidence = 1.0m;
                continue;
            }

            pendingAiIndexes.Add(items.Count - 1);
            pendingAiInputs.Add(categorizationInput);
        }

        if (pendingAiInputs.Count > 0)
        {
            var results = await PreviewManySafeAsync(customerId, pendingAiInputs, ct);
            for (var i = 0; i < pendingAiIndexes.Count && i < results.Count; i++)
            {
                var suggestion = results[i];
                var item = items[pendingAiIndexes[i]];
                item.CategoryId = suggestion.CategoryId;
                item.CategoryName = suggestion.CategoryName;
                item.Confidence = suggestion.Confidence;
            }
        }

        response.Rows.AddRange(items);
        return response;
    }

    /// <summary>Wraps PreviewManyAsync so a setup-time failure (e.g. the preference/category-catalog
    /// DB read) degrades every pending row to uncategorized instead of failing the whole extraction
    /// request — matching ApplyCategorizationAsync's single-row degrade-on-failure behavior.</summary>
    private async Task<IReadOnlyList<AiClassificationResult>> PreviewManySafeAsync(
        Guid customerId,
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        try
        {
            return await _categorization.PreviewManyAsync(customerId, inputs, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch category preview failed; returning {Count} rows uncategorized.", inputs.Count);
            return Array.Empty<AiClassificationResult>();
        }
    }

    public async Task CategorizeItemAsync(Guid customerId, ExtractedTransactionItem item, CancellationToken cancellationToken = default)
    {
        var rules = await _ruleService.GetRulesAsync(customerId, cancellationToken);
        await ApplyCategorizationAsync(customerId, item, item.Merchant ?? item.Description, rules, cancellationToken);
    }

    /// <summary>Rule-then-AI category suggestion shared by SMS/CSV's per-row loop and the
    /// single-row photo path. Mutates <paramref name="item"/> in place; AI failures degrade
    /// gracefully to a null suggestion (item still returned uncategorized).</summary>
    private async Task ApplyCategorizationAsync(
        Guid customerId,
        ExtractedTransactionItem item,
        string? categorizationInput,
        IReadOnlyList<RuleResponse> rules,
        CancellationToken ct)
    {
        // Only suggest a category for expenses (parsers/OCR emit uppercase "EXPENSE").
        if (!string.Equals(item.Type, "EXPENSE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(categorizationInput))
        {
            return;
        }

        // 1) Rule precedence: deterministic, no AI call.
        var ruleMatch = rules.FirstOrDefault(r =>
            categorizationInput.Contains(r.MerchantKeyword, StringComparison.OrdinalIgnoreCase));

        if (ruleMatch is not null)
        {
            item.CategoryId = ruleMatch.CategoryId;
            item.CategoryName = ruleMatch.CategoryName;
            item.Confidence = 1.0m;
            return;
        }

        // 2) Fall back to AI categorization.
        try
        {
            var suggestion = await _categorization.PreviewAsync(customerId, categorizationInput, ct);
            item.CategoryId = suggestion.CategoryId;
            item.CategoryName = suggestion.CategoryName;
            item.Confidence = suggestion.Confidence;
        }
        catch (Exception ex)
        {
            // AI down → leave the row uncategorized; the user resolves it on the review screen.
            _logger.LogWarning(ex, "Category preview failed during extract; returning row uncategorized.");
        }
    }
}
