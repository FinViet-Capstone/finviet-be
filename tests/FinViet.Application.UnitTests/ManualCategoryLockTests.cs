using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FinViet.Application.UnitTests;

public class ManualCategoryLockTests
{
    private static readonly DateTime TxnDate = new(2026, 9, 12, 5, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ai_suggestion")]
    [InlineData("ai_auto")]
    public async Task Classify_AfterAiSource_LocksRowAndLogsCorrection(string previousSource)
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: previousSource, guess: "cat_food", confidence: 0.9m);
        var repo = new TransactionRepository(db);

        var dto = await repo.ClassifyAsync(customerId, txn.TransactionId, "cat_transport");

        Assert.Equal("cat_transport", dto!.CategoryId);
        Assert.Equal("reviewed", dto.CategorizationStatus);
        Assert.Equal("manual", dto.AiSource);
        Assert.Null(dto.AiConfidence);

        var log = await db.CategoryCorrectionLogs.SingleAsync();
        Assert.Equal(customerId, log.CustomerId);
        Assert.Equal(txn.TransactionId, log.TransactionId);
        Assert.Equal("cat_transport", log.CorrectedCategoryId);
        Assert.Equal("Food", log.OriginalAiGuess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("manual")]
    [InlineData("merchant_rule")]
    public async Task Classify_AfterNonAiSource_LocksRowWithoutCorrectionLog(string? previousSource)
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: previousSource, guess: null, confidence: null);
        var repo = new TransactionRepository(db);

        var dto = await repo.ClassifyAsync(customerId, txn.TransactionId, "cat_transport");

        Assert.Equal("reviewed", dto!.CategorizationStatus);
        Assert.Equal("manual", dto.AiSource);
        Assert.Empty(db.CategoryCorrectionLogs);
    }

    [Fact]
    public async Task EditCategory_AfterAiSource_LocksRowAndLogsCorrection()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: "ai_suggestion", guess: "cat_food", confidence: 0.8m);
        var repo = new TransactionRepository(db);

        var dto = await repo.EditForCustomerAsync(
            customerId, txn.TransactionId, "cat_transport", null, null, null);

        Assert.Equal("reviewed", dto!.CategorizationStatus);
        Assert.Single(db.CategoryCorrectionLogs);
    }

    [Fact]
    public async Task EditWithoutCategory_LeavesAiStateAlone()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: "ai_suggestion", guess: "cat_food", confidence: 0.8m);
        var repo = new TransactionRepository(db);

        var dto = await repo.EditForCustomerAsync(
            customerId, txn.TransactionId, null, null, null, null);

        Assert.Equal("suggested", dto!.CategorizationStatus);
        Assert.Empty(db.CategoryCorrectionLogs);
    }

    [Fact]
    public async Task EditWithUnchangedCategory_LeavesAiStateAlone()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: "ai_suggestion", guess: "cat_food", confidence: 0.8m);
        txn.CategoryId = "cat_food";
        await db.SaveChangesAsync();
        var repo = new TransactionRepository(db);

        var dto = await repo.EditForCustomerAsync(
            customerId, txn.TransactionId, "cat_food", null, null, null);

        Assert.Equal("ai_suggestion", dto!.AiSource);
        Assert.Empty(db.CategoryCorrectionLogs);
    }

    [Fact]
    public async Task Override_RechecksBudgetForTransactionMonth()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, txn) = await SeedAsync(db, source: "ai_suggestion", guess: "cat_food", confidence: 0.8m);
        var budgets = new Mock<IBudgetService>();
        var service = new BeneficiaryRuleService(db, budgets.Object);

        var outcome = await service.OverrideCategoryAsync(
            customerId, txn.TransactionId, new OverrideCategoryRequest { CategoryId = "cat_food" });

        Assert.True(outcome.Applied);
        budgets.Verify(x => x.SyncBudgetOnTransactionChangeAsync(
            customerId, new DateOnly(2026, 9, 12), It.IsAny<CancellationToken>()), Times.Once);
        budgets.VerifyNoOtherCalls();
        var reloaded = await db.Transactions.SingleAsync();
        Assert.Equal("manual", reloaded.AiClassificationSource);
        Assert.Equal("cat_food", reloaded.CategoryId);
    }

    private static async Task<(Guid CustomerId, Transaction Txn)> SeedAsync(
        FinVietDbContext db, string? source, string? guess, decimal? confidence)
    {
        var customerId = Guid.NewGuid();
        var wallet = new Wallet
        {
            WalletId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletName = "Linked",
            WalletType = "sepay_linked",
            Balance = 1_000_000m
        };
        db.Categories.AddRange(
            new Category { CategoryId = "cat_food", CategoryName = "Food", Type = "expense" },
            new Category { CategoryId = "cat_transport", CategoryName = "Transport", Type = "expense" });
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = wallet.WalletId,
            TransactionType = "expense",
            EntryMethod = "sepay_sync",
            Amount = 50_000m,
            TransactionDate = TxnDate,
            AiClassificationSource = source,
            AiCategoryGuess = guess,
            AiConfidence = confidence,
            AiClassifiedAt = TxnDate
        };
        db.Wallets.Add(wallet);
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        return (customerId, txn);
    }
}
