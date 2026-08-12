using FinViet.Application.DTOs;
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

    public async Task<ExtractResponse> ExtractCsvAsync(Guid customerId, Stream fileStream, int? maxRows, CancellationToken cancellationToken = default)
    {
        var parsed = _bankParser.Parse(fileStream, maxRows);
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

        foreach (var row in parsed.Rows)
        {
            var item = new ExtractedTransactionItem
            {
                Amount = row.Amount,
                Type = row.TransactionType,
                Description = row.Note,
                Merchant = row.Note,
                TransactionDate = row.TransactionDate
            };

            // Only suggest a category for expenses (parsers emit uppercase "EXPENSE").
            if (string.Equals(row.TransactionType, "EXPENSE", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(row.Note))
            {
                // 1) Rule precedence: deterministic, no AI call.
                var ruleMatch = rules.FirstOrDefault(r =>
                    row.Note!.Contains(r.MerchantKeyword, StringComparison.OrdinalIgnoreCase));

                if (ruleMatch is not null)
                {
                    item.CategoryId = ruleMatch.CategoryId;
                    item.CategoryName = ruleMatch.CategoryName;
                    item.Confidence = 1.0m;
                }
                else
                {
                    // 2) Fall back to AI categorization.
                    try
                    {
                        var suggestion = await _categorization.PreviewAsync(customerId, row.Note, ct);
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

            response.Rows.Add(item);
        }

        return response;
    }
}
