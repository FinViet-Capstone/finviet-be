using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests;

public class SepayCategorizationWorkerTests
{
    [Fact]
    public async Task ProcessBatchAsync_AppliedRows_RechecksBudgetOncePerAffectedMonth()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var septA = Expense(customerId, new DateTime(2026, 9, 3, 5, 0, 0, DateTimeKind.Utc));
        var septB = Expense(customerId, new DateTime(2026, 9, 20, 5, 0, 0, DateTimeKind.Utc));
        var aug = Expense(customerId, new DateTime(2026, 8, 20, 5, 0, 0, DateTimeKind.Utc));
        var suggested = Expense(customerId, new DateTime(2026, 7, 20, 5, 0, 0, DateTimeKind.Utc));
        db.Transactions.AddRange(septA, septB, aug, suggested);
        await db.SaveChangesAsync();

        var categorization = new Mock<IAiCategorizationService>();
        categorization.Setup(x => x.CategorizeManyAsync(
                customerId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CategorizationOutcome { TransactionId = septA.TransactionId, Applied = true },
                new CategorizationOutcome { TransactionId = septB.TransactionId, Applied = true },
                new CategorizationOutcome { TransactionId = aug.TransactionId, Applied = true },
                new CategorizationOutcome { TransactionId = suggested.TransactionId, Applied = false }
            });
        var budgets = new Mock<IBudgetService>();
        var worker = CreateWorker(db, categorization.Object, budgets.Object);

        await worker.ProcessBatchAsync(
            new SepayCategorizationBatch(
                customerId,
                new[] { septA.TransactionId, septB.TransactionId, aug.TransactionId, suggested.TransactionId }),
            CancellationToken.None);

        budgets.Verify(x => x.SyncBudgetOnTransactionChangeAsync(
            customerId, new DateOnly(2026, 9, 1), It.IsAny<CancellationToken>()), Times.Once);
        budgets.Verify(x => x.SyncBudgetOnTransactionChangeAsync(
            customerId, new DateOnly(2026, 8, 1), It.IsAny<CancellationToken>()), Times.Once);
        budgets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessBatchAsync_NothingApplied_SkipsBudgetRecheck()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var transaction = Expense(customerId, new DateTime(2026, 9, 3, 5, 0, 0, DateTimeKind.Utc));
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var categorization = new Mock<IAiCategorizationService>();
        categorization.Setup(x => x.CategorizeManyAsync(
                customerId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CategorizationOutcome { TransactionId = transaction.TransactionId, Applied = false } });
        var budgets = new Mock<IBudgetService>(MockBehavior.Strict);
        var worker = CreateWorker(db, categorization.Object, budgets.Object);

        await worker.ProcessBatchAsync(
            new SepayCategorizationBatch(customerId, new[] { transaction.TransactionId }),
            CancellationToken.None);

        budgets.VerifyNoOtherCalls();
    }

    [Fact]
    public void Queue_SplitsIdsIntoBatchesOfTwenty()
    {
        var queue = new SepayCategorizationQueue();
        var customerId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 45).Select(_ => Guid.NewGuid()).ToList();

        queue.Enqueue(customerId, ids);

        var sizes = new List<int>();
        while (queue.Reader.TryRead(out var batch))
        {
            Assert.Equal(customerId, batch.CustomerId);
            sizes.Add(batch.TransactionIds.Count);
        }
        Assert.Equal(new[] { 20, 20, 5 }, sizes);
    }

    private static SepayCategorizationWorker CreateWorker(
        FinVietDbContext db,
        IAiCategorizationService categorization,
        IBudgetService budgets)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(categorization);
        services.AddSingleton(budgets);
        var provider = services.BuildServiceProvider();
        return new SepayCategorizationWorker(
            new SepayCategorizationQueue(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SepayCategorizationWorker>.Instance);
    }

    private static Transaction Expense(Guid customerId, DateTime date) => new()
    {
        TransactionId = Guid.NewGuid(),
        CustomerId = customerId,
        WalletId = Guid.NewGuid(),
        Amount = 50_000m,
        TransactionType = "expense",
        EntryMethod = "sepay_sync",
        TransactionDate = date,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
