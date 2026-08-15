using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests;

public class AiCategorizationServiceTests
{
    [Fact]
    public async Task CategorizeTransactionAsync_OtherCustomerTransaction_ReturnsNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var ownerId = Guid.NewGuid();
        var transaction = TransactionFor(ownerId);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        var service = CreateService(db, model, new Mock<IMerchantRuleService>(MockBehavior.Strict));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CategorizeTransactionAsync(Guid.NewGuid(), transaction.TransactionId));
        model.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CategorizeTransactionAsync_ModeOff_DoesNotCallGemini()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = TransactionFor(customerId);
        db.Transactions.Add(transaction);
        db.AiCustomerPreferences.Add(Preference(customerId, "off", 0.85m));
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        var rules = NoRule(customerId, transaction);
        var service = CreateService(db, model, rules);

        var result = await service.CategorizeTransactionAsync(customerId, transaction.TransactionId);

        Assert.Equal("OFF", result.Source);
        Assert.False(result.Applied);
        model.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CategorizeTransactionAsync_SuggestOnly_DoesNotOverwriteCategory()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = TransactionFor(customerId, categoryId: "cat_original");
        db.Categories.AddRange(
            Category("cat_original", "Ban đầu"),
            Category("cat_food", "Ăn uống"));
        db.Transactions.Add(transaction);
        db.AiCustomerPreferences.Add(Preference(customerId, "suggest_only", 0.85m));
        await db.SaveChangesAsync();
        var model = Classifier("Ăn uống", 0.99m);
        var service = CreateService(db, model, NoRule(customerId, transaction));

        var result = await service.CategorizeTransactionAsync(customerId, transaction.TransactionId);

        Assert.False(result.Applied);
        Assert.Equal("AI_SUGGESTION", result.Source);
        Assert.Equal("cat_original", transaction.CategoryId);
        Assert.Equal("cat_food", transaction.AiCategoryGuess);
        Assert.Equal("ai_suggestion", transaction.AiClassificationSource);
    }

    [Fact]
    public async Task CategorizeTransactionAsync_ExactThreshold_AppliesCategory()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = TransactionFor(customerId);
        db.Categories.Add(Category("cat_food", "Ăn uống"));
        db.Transactions.Add(transaction);
        db.AiCustomerPreferences.Add(Preference(customerId, "high_confidence_auto", 0.85m));
        await db.SaveChangesAsync();
        var service = CreateService(db, Classifier("Ăn uống", 0.85m), NoRule(customerId, transaction));

        var result = await service.CategorizeTransactionAsync(customerId, transaction.TransactionId);

        Assert.True(result.Applied);
        Assert.Equal("AI_AUTO", result.Source);
        Assert.Equal("cat_food", transaction.CategoryId);
        Assert.True(transaction.IsAiClassified);
    }

    [Fact]
    public async Task CategorizeTransactionAsync_ManualSource_WinsWithoutRuleOrGemini()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = TransactionFor(customerId, categoryId: "cat_manual");
        transaction.AiClassificationSource = "manual";
        db.Categories.Add(Category("cat_manual", "Đã chọn"));
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        var rules = new Mock<IMerchantRuleService>(MockBehavior.Strict);
        var telemetry = Telemetry();
        var service = CreateService(db, model, rules, telemetry: telemetry);

        var result = await service.CategorizeTransactionAsync(customerId, transaction.TransactionId);

        Assert.Equal("MANUAL", result.Source);
        Assert.Equal("cat_manual", result.CategoryId);
        telemetry.Verify(x => x.RecordAuditAsync(
            It.Is<AiAuditRecord>(record =>
                record.EventType == "categorization_decision"
                && Equals(record.Metadata!["source"], "manual")
                && Equals(record.Metadata["reason"], "manual_locked")),
            It.IsAny<CancellationToken>()), Times.Once);
        model.VerifyNoOtherCalls();
        rules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CategorizeTransactionAsync_RateLimited_UsesFallbackWithoutGemini()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = TransactionFor(customerId);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        var limiter = new Mock<IAiRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.TryAcquireAsync(
                customerId,
                "classification",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var telemetry = Telemetry();
        var service = CreateService(
            db,
            model,
            NoRule(customerId, transaction),
            limiter,
            telemetry);

        var result = await service.CategorizeTransactionAsync(customerId, transaction.TransactionId);

        Assert.Equal("FALLBACK", result.Source);
        Assert.Equal("rate_limited", result.Reason);
        telemetry.Verify(x => x.RecordUsageAsync(
            It.Is<AiUsageRecord>(record =>
                record.Feature == "classification"
                && record.Outcome == "rate_limited"
                && record.CustomerId == customerId),
            It.IsAny<CancellationToken>()), Times.Once);
        model.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_DoesNotExposeAnotherCustomersCustomCategory()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        db.Categories.AddRange(
            Category("cat_food", "Ăn uống"),
            Category("custom_secret", "Danh mục riêng"));
        db.CustomerCategories.Add(new CustomerCategory
        {
            CustomerId = otherCustomerId,
            CategoryId = "custom_secret",
            BucketId = "needs",
            Source = "persona",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>();
        IReadOnlyList<string>? allowed = null;
        model.Setup(x => x.ClassifyAsync(
                "test",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AiRequestContext>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken, AiRequestContext?>((_, categories, _, _) => allowed = categories)
            .ReturnsAsync(new AiClassificationResult());
        var service = CreateService(db, model, new Mock<IMerchantRuleService>());

        await service.PreviewAsync(customerId, "test");

        Assert.NotNull(allowed);
        Assert.Contains("Ăn uống", allowed!);
        Assert.DoesNotContain("Danh mục riêng", allowed!);
    }

    private static AiCategorizationService CreateService(
        FinViet.Infrastructure.Persistence.Context.FinVietDbContext db,
        Mock<IAiModelClient> model,
        Mock<IMerchantRuleService> rules,
        Mock<IAiRateLimiter>? rateLimiter = null,
        Mock<IAiTelemetryRecorder>? telemetry = null)
        => new(
            db,
            model.Object,
            rules.Object,
            (rateLimiter ?? AllowRateLimit()).Object,
            (telemetry ?? Telemetry()).Object,
            NullLogger<AiCategorizationService>.Instance);

    private static Mock<IAiTelemetryRecorder> Telemetry()
    {
        var telemetry = new Mock<IAiTelemetryRecorder>();
        telemetry.Setup(x => x.RecordAuditAsync(
                It.IsAny<AiAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        telemetry.Setup(x => x.RecordUsageAsync(
                It.IsAny<AiUsageRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return telemetry;
    }

    private static Mock<IAiRateLimiter> AllowRateLimit()
    {
        var limiter = new Mock<IAiRateLimiter>();
        limiter.Setup(x => x.TryAcquireAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return limiter;
    }

    private static Mock<IMerchantRuleService> NoRule(Guid customerId, Transaction transaction)
    {
        var rules = new Mock<IMerchantRuleService>(MockBehavior.Strict);
        rules.Setup(x => x.ResolveAsync(
                customerId,
                transaction.Merchant,
                transaction.Description,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuleMatch?)null);
        return rules;
    }

    private static Mock<IAiModelClient> Classifier(string categoryName, decimal confidence)
    {
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        model.Setup(x => x.ClassifyAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AiRequestContext>()))
            .ReturnsAsync(new AiClassificationResult
            {
                CategoryName = categoryName,
                Confidence = confidence
            });
        return model;
    }

    private static Transaction TransactionFor(Guid customerId, string? categoryId = null) => new()
    {
        TransactionId = Guid.NewGuid(),
        CustomerId = customerId,
        WalletId = Guid.NewGuid(),
        CategoryId = categoryId,
        Amount = 100_000m,
        TransactionType = "expense",
        Merchant = "Highlands",
        EntryMethod = "manual",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Category Category(string id, string name) => new()
    {
        CategoryId = id,
        CategoryName = name,
        Type = "expense"
    };

    private static AiCustomerPreference Preference(Guid customerId, string mode, decimal threshold) => new()
    {
        CustomerId = customerId,
        CategorizationMode = mode,
        AutoCategorizationThreshold = threshold,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
