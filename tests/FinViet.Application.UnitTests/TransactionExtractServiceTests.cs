using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests;

public class TransactionExtractServiceTests
{
    [Fact]
    public async Task ExtractSmsAsync_AiSuggestion_SetsCategoryIdFromPreview()
    {
        var customerId = Guid.NewGuid();
        var smsParser = SmsParserFor(Row("EXPENSE", "Highlands Coffee"));
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        categorization
            .Setup(x => x.PreviewManyAsync(
                customerId,
                It.Is<IReadOnlyList<string>>(inputs => inputs.SequenceEqual(new[] { "Highlands Coffee" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AiClassificationResult>
            {
                new()
                {
                    CategoryId = "cat_food",
                    CategoryName = "Ăn uống",
                    Confidence = 0.9m
                }
            });
        var service = CreateService(smsParser, NoRule(), categorization);

        var result = await service.ExtractSmsAsync(customerId, "any text");

        var item = Assert.Single(result.Rows);
        Assert.Equal("cat_food", item.CategoryId);
        Assert.Equal("Ăn uống", item.CategoryName);
        Assert.Equal(0.9m, item.Confidence);
    }

    [Fact]
    public async Task ExtractSmsAsync_RuleMatch_TakesPrecedenceOverAi()
    {
        var customerId = Guid.NewGuid();
        var smsParser = SmsParserFor(Row("EXPENSE", "Highlands Coffee"));
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        var rules = new Mock<IMerchantRuleService>();
        rules.Setup(x => x.GetRulesAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RuleResponse>
            {
                new() { RuleId = Guid.NewGuid(), MerchantKeyword = "Highlands", CategoryId = "cat_rule", CategoryName = "Quy tắc" }
            });
        var service = CreateService(smsParser, rules, categorization);

        var result = await service.ExtractSmsAsync(customerId, "any text");

        var item = Assert.Single(result.Rows);
        Assert.Equal("cat_rule", item.CategoryId);
        Assert.Equal(1.0m, item.Confidence);
        categorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtractSmsAsync_AiThrows_RowStaysUncategorized()
    {
        var customerId = Guid.NewGuid();
        var smsParser = SmsParserFor(Row("EXPENSE", "Highlands Coffee"));
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        categorization
            .Setup(x => x.PreviewManyAsync(
                customerId,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var service = CreateService(smsParser, NoRule(), categorization);

        var result = await service.ExtractSmsAsync(customerId, "any text");

        var item = Assert.Single(result.Rows);
        Assert.Null(item.CategoryId);
        Assert.Null(item.CategoryName);
    }

    [Fact]
    public async Task ExtractSmsAsync_IncomeRow_SkipsCategorizationEntirely()
    {
        var customerId = Guid.NewGuid();
        var smsParser = SmsParserFor(Row("INCOME", "Lương tháng 8"));
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        var rules = new Mock<IMerchantRuleService>(MockBehavior.Strict);
        rules.Setup(x => x.GetRulesAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RuleResponse>());
        var service = CreateService(smsParser, rules, categorization);

        var result = await service.ExtractSmsAsync(customerId, "any text");

        var item = Assert.Single(result.Rows);
        Assert.Null(item.CategoryId);
        categorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtractSmsAsync_MultipleRows_RuleMatchedRowsSkipTheBatchAiCallEntirely()
    {
        var customerId = Guid.NewGuid();
        var smsParser = SmsParserFor(
            Row("EXPENSE", "Grab ride"),
            Row("EXPENSE", "Highlands Coffee"),
            Row("EXPENSE", "Circle K"));
        var rules = new Mock<IMerchantRuleService>();
        rules.Setup(x => x.GetRulesAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RuleResponse>
            {
                new() { RuleId = Guid.NewGuid(), MerchantKeyword = "Grab", CategoryId = "cat_transport", CategoryName = "Di chuyển" }
            });
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        categorization
            .Setup(x => x.PreviewManyAsync(
                customerId,
                It.Is<IReadOnlyList<string>>(inputs => inputs.SequenceEqual(new[] { "Highlands Coffee", "Circle K" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AiClassificationResult>
            {
                new() { CategoryId = "cat_food", CategoryName = "Ăn uống", Confidence = 0.9m },
                new() { CategoryId = "cat_shopping", CategoryName = "Mua sắm", Confidence = 0.7m }
            });
        var service = CreateService(smsParser, rules, categorization);

        var result = await service.ExtractSmsAsync(customerId, "any text");

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("cat_transport", result.Rows[0].CategoryId); // rule match, no AI call
        Assert.Equal("cat_food", result.Rows[1].CategoryId);
        Assert.Equal("cat_shopping", result.Rows[2].CategoryId);
    }

    [Fact]
    public async Task CategorizeItemAsync_PhotoRow_ResolvesCategoryIdFromMerchant()
    {
        var customerId = Guid.NewGuid();
        var categorization = new Mock<IAiCategorizationService>(MockBehavior.Strict);
        categorization
            .Setup(x => x.PreviewAsync(customerId, "Circle K", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiClassificationResult
            {
                CategoryId = "cat_shopping",
                CategoryName = "Mua sắm",
                Confidence = 0.8m
            });
        var service = CreateService(SmsParserFor(), NoRule(), categorization);
        var item = new ExtractedTransactionItem
        {
            Amount = 50_000m,
            Type = "EXPENSE",
            Merchant = "Circle K",
            TransactionDate = DateTime.UtcNow
        };

        await service.CategorizeItemAsync(customerId, item);

        Assert.Equal("cat_shopping", item.CategoryId);
        Assert.Equal("Mua sắm", item.CategoryName);
    }

    private static TransactionExtractService CreateService(
        Mock<ISmsTransactionParser> smsParser,
        Mock<IMerchantRuleService> rules,
        Mock<IAiCategorizationService> categorization)
        => new(
            smsParser.Object,
            new Mock<IBankStatementParser>(MockBehavior.Strict).Object,
            categorization.Object,
            rules.Object,
            NullLogger<TransactionExtractService>.Instance);

    private static Mock<IMerchantRuleService> NoRule()
    {
        var rules = new Mock<IMerchantRuleService>();
        rules.Setup(x => x.GetRulesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RuleResponse>());
        return rules;
    }

    private static Mock<ISmsTransactionParser> SmsParserFor(params ParsedTransactionDto[] rows)
    {
        var parser = new Mock<ISmsTransactionParser>();
        parser.Setup(x => x.Parse(It.IsAny<string>())).Returns(new ParseResult
        {
            Rows = rows.ToList(),
            TotalRowsScanned = rows.Length,
            SkippedDuringParse = 0
        });
        return parser;
    }

    private static ParsedTransactionDto Row(string type, string note) => new()
    {
        TransactionType = type,
        Amount = 50_000m,
        TransactionDate = DateTime.UtcNow,
        Note = note,
        RawText = note
    };
}
