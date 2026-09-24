using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.Ai.Commands.OverrideCategoryBatch;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Features.Ai.Commands.OverrideCategoryBatch;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FinViet.Application.UnitTests;

public class OverrideCategoryBatchTests
{
    private static readonly DateTime SeptemberDate = new(2026, 9, 12, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberDate = new(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MixedBatch_ReportsPerItemOutcome_AndAppliesOnlyValidRows()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, wallet) = await SeedCustomerAsync(db);
        var valid = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate, "ai_suggestion");
        var unavailableCategory = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate, "ai_suggestion");
        var income = await AddTransactionAsync(db, customerId, wallet, "income", SeptemberDate, null);
        var (_, otherWallet) = await SeedCustomerAsync(db);
        var foreign = await AddTransactionAsync(db, Guid.NewGuid(), otherWallet, "expense", SeptemberDate, "ai_suggestion");
        var missing = Guid.NewGuid();
        var budgets = new Mock<IBudgetService>();

        var response = await HandleAsync(db, budgets, customerId,
            Item(valid, "cat_food"),
            Item(foreign, "cat_food"),
            Item(missing, "cat_food"),
            Item(unavailableCategory, "cat_nope"),
            Item(income, "cat_food"));

        Assert.Equal(
            new[]
            {
                (valid, "ok"),
                (foreign, "not_found"),
                (missing, "not_found"),
                (unavailableCategory, "category_unavailable"),
                (income, "not_eligible")
            },
            response.Results.Select(r => (r.TransactionId, r.Outcome)).ToArray());

        var applied = await db.Transactions.SingleAsync(t => t.TransactionId == valid);
        Assert.Equal("cat_food", applied.CategoryId);
        Assert.Equal("manual", applied.AiClassificationSource);

        foreach (var untouched in new[] { foreign, unavailableCategory, income })
        {
            var row = await db.Transactions.SingleAsync(t => t.TransactionId == untouched);
            Assert.Null(row.CategoryId);
            Assert.NotEqual("manual", row.AiClassificationSource);
        }

        var log = await db.CategoryCorrectionLogs.SingleAsync();
        Assert.Equal(valid, log.TransactionId);
    }

    [Fact]
    public async Task AppliedRows_EachGetOneCorrectionLog_AndBudgetsRecheckOncePerMonth()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, wallet) = await SeedCustomerAsync(db);
        var sept1 = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate, "ai_suggestion");
        var sept2 = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate.AddDays(3), "ai_suggestion");
        var oct = await AddTransactionAsync(db, customerId, wallet, "expense", OctoberDate, "ai_suggestion");
        var budgets = new Mock<IBudgetService>();

        await HandleAsync(db, budgets, customerId,
            Item(sept1, "cat_food"), Item(sept2, "cat_transport"), Item(oct, "cat_food"));

        Assert.Equal(3, await db.CategoryCorrectionLogs.CountAsync());
        budgets.Verify(x => x.SyncBudgetOnTransactionChangeAsync(
            customerId, It.Is<DateOnly>(d => d.Year == 2026 && d.Month == 9), It.IsAny<CancellationToken>()), Times.Once);
        budgets.Verify(x => x.SyncBudgetOnTransactionChangeAsync(
            customerId, It.Is<DateOnly>(d => d.Year == 2026 && d.Month == 10), It.IsAny<CancellationToken>()), Times.Once);
        budgets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NothingApplied_SkipsBudgetRecheck()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, _) = await SeedCustomerAsync(db);
        var budgets = new Mock<IBudgetService>();

        await HandleAsync(db, budgets, customerId, Item(Guid.NewGuid(), "cat_food"));

        budgets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CustomCategory_IsAvailableOnlyToItsOwner()
    {
        await using var db = TestDbContextFactory.Create();
        var (customerId, wallet) = await SeedCustomerAsync(db);
        var owned = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate, null);
        var other = await AddTransactionAsync(db, customerId, wallet, "expense", SeptemberDate, null);
        db.Categories.AddRange(
            new Category { CategoryId = "custom_mine", CategoryName = "Mine", Type = "expense" },
            new Category { CategoryId = "custom_theirs", CategoryName = "Theirs", Type = "expense" });
        db.CustomerCategories.AddRange(
            new CustomerCategory { CustomerId = customerId, CategoryId = "custom_mine", BucketId = "needs", IsActive = true },
            new CustomerCategory { CustomerId = Guid.NewGuid(), CategoryId = "custom_theirs", BucketId = "needs", IsActive = true });
        await db.SaveChangesAsync();

        var response = await HandleAsync(db, new Mock<IBudgetService>(), customerId,
            Item(owned, "custom_mine"), Item(other, "custom_theirs"));

        Assert.Equal(new[] { "ok", "category_unavailable" }, response.Results.Select(r => r.Outcome).ToArray());
    }

    [Fact]
    public void Validator_RejectsMoreThan200Items()
    {
        var validator = new OverrideCategoryBatchCommandValidator();

        Assert.True(validator.Validate(Command(200)).IsValid);
        Assert.False(validator.Validate(Command(201)).IsValid);
    }

    [Fact]
    public void Validator_RejectsEmptyBlankAndDuplicateItems()
    {
        var validator = new OverrideCategoryBatchCommandValidator();
        var id = Guid.NewGuid();

        Assert.False(validator.Validate(Command(0)).IsValid);
        Assert.False(validator.Validate(new OverrideCategoryBatchCommand(Guid.NewGuid(),
            new OverrideCategoryBatchRequest { Items = { Item(id, "cat_food"), Item(id, "cat_food") } })).IsValid);
        Assert.False(validator.Validate(new OverrideCategoryBatchCommand(Guid.NewGuid(),
            new OverrideCategoryBatchRequest { Items = { Item(id, "") } })).IsValid);
    }

    private static OverrideCategoryBatchCommand Command(int count) => new(
        Guid.NewGuid(),
        new OverrideCategoryBatchRequest
        {
            Items = Enumerable.Range(0, count).Select(_ => Item(Guid.NewGuid(), "cat_food")).ToList()
        });

    private static OverrideCategoryBatchItem Item(Guid transactionId, string categoryId) =>
        new() { TransactionId = transactionId, CategoryId = categoryId };

    private static Task<OverrideCategoryBatchResponse> HandleAsync(
        FinVietDbContext db, Mock<IBudgetService> budgets, Guid customerId, params OverrideCategoryBatchItem[] items) =>
        new OverrideCategoryBatchCommandHandler(db, budgets.Object).Handle(
            new OverrideCategoryBatchCommand(customerId, new OverrideCategoryBatchRequest { Items = items.ToList() }),
            CancellationToken.None);

    private static async Task<(Guid CustomerId, Guid WalletId)> SeedCustomerAsync(FinVietDbContext db)
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
        db.Wallets.Add(wallet);
        if (!await db.Categories.AnyAsync())
            db.Categories.AddRange(
                new Category { CategoryId = "cat_food", CategoryName = "Food", Type = "expense" },
                new Category { CategoryId = "cat_transport", CategoryName = "Transport", Type = "expense" });
        await db.SaveChangesAsync();
        return (customerId, wallet.WalletId);
    }

    private static async Task<Guid> AddTransactionAsync(
        FinVietDbContext db, Guid customerId, Guid walletId, string type, DateTime date, string? source)
    {
        var txn = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            TransactionType = type,
            EntryMethod = "sepay_sync",
            Amount = 50_000m,
            TransactionDate = date,
            AiClassificationSource = source,
            AiCategoryGuess = source is null ? null : "cat_food",
            AiConfidence = source is null ? null : 0.8m,
            AiClassifiedAt = date
        };
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        return txn.TransactionId;
    }
}
