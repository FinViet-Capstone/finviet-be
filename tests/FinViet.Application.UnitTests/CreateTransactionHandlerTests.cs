using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.DTOs.Categories;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Features.Transactions.Commands;
using FinViet.Application.Features.Transactions.Handlers;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests;

public class CreateTransactionHandlerTests
{
    [Theory]
    [InlineData("AI_BATCH", "ai_suggestion")]
    [InlineData("RULE", "merchant_rule")]
    public async Task Handle_CsvLegacySource_PersistsCanonicalSource(string source, string expected)
    {
        var repo = RepoReturning(new TransactionResponseDto { TransactionId = Guid.NewGuid() });
        var handler = CreateHandler(repo, new Mock<IMerchantRuleService>(), new Mock<IAiTelemetryRecorder>());
        await handler.Handle(new CreateTransactionCommand
        {
            CustomerId = Guid.NewGuid(), WalletId = Guid.NewGuid(), CategoryId = "cat_food",
            TransactionType = "expense", Amount = 50_000m, TransactionDate = DateTime.UtcNow,
            Note = "Ca phe Highlands", EntryMethod = "csv_import", AiSource = source,
        }, CancellationToken.None);

        repo.Verify(r => r.CreateManualForCustomerAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "cat_food", "expense", 50_000m,
            It.IsAny<DateTime>(), "Ca phe Highlands", It.IsAny<string?>(), "csv_import",
            It.IsAny<string?>(), expected, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidAiSource_RejectsBeforeRepositoryWrite()
    {
        var repo = RepoReturning(new TransactionResponseDto());
        var handler = CreateHandler(repo, new Mock<IMerchantRuleService>(), new Mock<IAiTelemetryRecorder>());
        await Assert.ThrowsAsync<FinViet.Application.Common.Exceptions.BadRequestException>(() =>
            handler.Handle(new CreateTransactionCommand
            {
                TransactionType = "expense", Amount = 50_000m, AiSource = "unknown",
            }, CancellationToken.None));
        repo.VerifyNoOtherCalls();
    }

    private static CreateTransactionHandler CreateHandler(
        Mock<ITransactionRepository> repo,
        Mock<IMerchantRuleService> rules,
        Mock<IAiTelemetryRecorder> telemetry)
    {
        var categories = new Mock<ICategoryService>();
        categories
            .Setup(c => c.GetCategoryByIdAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryResponse { CategoryId = "cat_food", CategoryName = "Ăn uống", Type = "expense" });

        var budget = new Mock<IBudgetService>();
        budget
            .Setup(b => b.SyncBudgetOnTransactionChangeAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CreateTransactionHandler(
            repo.Object,
            categories.Object,
            rules.Object,
            budget.Object,
            telemetry.Object,
            NullLogger<CreateTransactionHandler>.Instance);
    }

    private static Mock<ITransactionRepository> RepoReturning(TransactionResponseDto response)
    {
        var repo = new Mock<ITransactionRepository>();
        repo
            .Setup(r => r.CreateManualForCustomerAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<decimal?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return repo;
    }

    [Fact]
    public async Task Handle_PassesMerchantThroughToRepository()
    {
        var response = new TransactionResponseDto { TransactionId = Guid.NewGuid() };
        var repo = RepoReturning(response);
        var rules = new Mock<IMerchantRuleService>();
        var telemetry = new Mock<IAiTelemetryRecorder>();
        var handler = CreateHandler(repo, rules, telemetry);

        await handler.Handle(new CreateTransactionCommand
        {
            CustomerId = Guid.NewGuid(),
            WalletId = Guid.NewGuid(),
            CategoryId = "cat_food",
            TransactionType = "expense",
            Amount = 100_000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Thanh toan hoa don",
            Merchant = "EVN HO CHI MINH",
        }, CancellationToken.None);

        repo.Verify(r => r.CreateManualForCustomerAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "cat_food", "expense", 100_000m, It.IsAny<DateTime>(),
            "Thanh toan hoa don", It.IsAny<string?>(), It.IsAny<string?>(), "EVN HO CHI MINH",
            It.IsAny<string?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AiSourceProvided_WritesCategorizationDecisionAuditRecord()
    {
        var transactionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var response = new TransactionResponseDto { TransactionId = transactionId };
        var repo = RepoReturning(response);
        var rules = new Mock<IMerchantRuleService>();
        var telemetry = new Mock<IAiTelemetryRecorder>();
        var handler = CreateHandler(repo, rules, telemetry);

        await handler.Handle(new CreateTransactionCommand
        {
            CustomerId = customerId,
            WalletId = Guid.NewGuid(),
            CategoryId = "cat_food",
            TransactionType = "expense",
            Amount = 50_000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Ca phe",
            Merchant = "Highlands Coffee",
            AiSource = "AI_BATCH",
            AiConfidence = 0.92m,
        }, CancellationToken.None);

        telemetry.Verify(t => t.RecordAuditAsync(
            It.Is<AiAuditRecord>(a =>
                a.EventType == "categorization_decision"
                && a.CorrelationId == transactionId
                && a.CustomerId == customerId
                && (string?)a.Metadata!["source"] == "ai_suggestion"
                && (decimal?)a.Metadata["confidence"] == 0.92m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoAiSourceAndNoRuleMatch_DoesNotWriteAuditRecord()
    {
        var response = new TransactionResponseDto { TransactionId = Guid.NewGuid() };
        var repo = RepoReturning(response);
        var rules = new Mock<IMerchantRuleService>();
        var telemetry = new Mock<IAiTelemetryRecorder>();
        var handler = CreateHandler(repo, rules, telemetry);

        await handler.Handle(new CreateTransactionCommand
        {
            CustomerId = Guid.NewGuid(),
            WalletId = Guid.NewGuid(),
            CategoryId = "cat_food",
            TransactionType = "expense",
            Amount = 20_000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Manual entry",
        }, CancellationToken.None);

        telemetry.Verify(t => t.RecordAuditAsync(It.IsAny<AiAuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RuleMatchAppliesCategory_LogsRuleSourceNotClientAiSource()
    {
        var transactionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var response = new TransactionResponseDto { TransactionId = transactionId };
        var repo = RepoReturning(response);
        var ruleId = Guid.NewGuid();
        var rules = new Mock<IMerchantRuleService>();
        rules
            .Setup(r => r.ResolveAsync(customerId, null, "Coffee run", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleMatch(ruleId, "cat_food", "Ăn uống", "coffee"));
        rules
            .Setup(r => r.IncrementAppliedAsync(ruleId, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var telemetry = new Mock<IAiTelemetryRecorder>();
        var handler = CreateHandler(repo, rules, telemetry);

        // No CategoryId and a client-supplied AiSource — the rule match must win, and the
        // stale client-side AI suggestion must not be what gets logged as the decision source.
        await handler.Handle(new CreateTransactionCommand
        {
            CustomerId = customerId,
            WalletId = Guid.NewGuid(),
            CategoryId = null,
            TransactionType = "expense",
            Amount = 30_000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Coffee run",
            AiSource = "AI_BATCH",
            AiConfidence = 0.5m,
        }, CancellationToken.None);

        telemetry.Verify(t => t.RecordAuditAsync(
            It.Is<AiAuditRecord>(a => (string?)a.Metadata!["source"] == "merchant_rule"),
            It.IsAny<CancellationToken>()), Times.Once);
        rules.Verify(r => r.IncrementAppliedAsync(ruleId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}
