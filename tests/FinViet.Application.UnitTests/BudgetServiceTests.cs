using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Application.UnitTests;

// Savings-bucket goal netting: a saving-goal contribution/withdrawal is functionally the
// customer fulfilling their Savings allocation, so GetBudgetBucketsAsync nets cat_savings_goal
// transactions (contribution-expense minus withdrawal-income, floored at 0) into the Savings
// bucket's Spent instead of excluding them outright — matching the mobile client's
// useBucketSpend convention exactly.
public class BudgetServiceTests
{
    private const string CurrentMonth = "2026-05";

    [Fact]
    public async Task GetBudgetBuckets_SavingsBucket_NetsGoalContributionsMinusWithdrawals()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        db.Customers.Add(Customer(customerId, income: 10_000_000m));
        db.Wallets.Add(Wallet(walletId, customerId));
        db.Categories.AddRange(
            Category("cat_savings", "Tiết kiệm", "savings"),
            Category("cat_savings_goal", "Đóng góp mục tiêu", "savings"));
        db.Transactions.AddRange(
            Tx(customerId, walletId, "cat_savings", "expense", 1_000_000m, "2026-05-05"),  // ordinary savings spend
            Tx(customerId, walletId, "cat_savings_goal", "expense", 5_000_000m, "2026-05-10"), // goal contribution
            Tx(customerId, walletId, "cat_savings_goal", "income", 2_000_000m, "2026-05-15"));  // goal withdrawal
        await db.SaveChangesAsync();

        var result = await Service(db).GetBudgetBucketsAsync(customerId, CurrentMonth);

        var savings = result.Buckets.Single(b => b.Bucket == "savings");
        // 1,000,000 (category spend) + max(0, 5,000,000 - 2,000,000) (net goal) = 4,000,000
        Assert.Equal(4_000_000m, savings.Spent);
    }

    [Fact]
    public async Task GetBudgetBuckets_SavingsBucket_WithdrawalExceedingContributions_FloorsGoalNetButKeepsCategorySpend()
    {
        // The exact mobile-reported data-loss repro (finding C1): a goal withdrawal larger than
        // this month's contributions must never erase an unrelated ordinary savings expense —
        // only the goal component floors at 0, not the whole bucket.
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        db.Customers.Add(Customer(customerId, income: 10_000_000m));
        db.Wallets.Add(Wallet(walletId, customerId));
        db.Categories.AddRange(
            Category("cat_savings", "Tiết kiệm", "savings"),
            Category("cat_savings_goal", "Đóng góp mục tiêu", "savings"));
        db.Transactions.AddRange(
            Tx(customerId, walletId, "cat_savings", "expense", 2_000_000m, "2026-05-05"),
            Tx(customerId, walletId, "cat_savings_goal", "income", 5_000_000m, "2026-05-20")); // withdrawal, no contribution this month
        await db.SaveChangesAsync();

        var result = await Service(db).GetBudgetBucketsAsync(customerId, CurrentMonth);

        var savings = result.Buckets.Single(b => b.Bucket == "savings");
        Assert.Equal(2_000_000m, savings.Spent);
    }

    [Fact]
    public async Task GetBudgetBuckets_NoGoalActivity_SavingsBucketUnaffected()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();

        db.Customers.Add(Customer(customerId, income: 10_000_000m));
        db.Wallets.Add(Wallet(walletId, customerId));
        db.Categories.Add(Category("cat_savings", "Tiết kiệm", "savings"));
        db.Transactions.Add(Tx(customerId, walletId, "cat_savings", "expense", 1_500_000m, "2026-05-05"));
        await db.SaveChangesAsync();

        var result = await Service(db).GetBudgetBucketsAsync(customerId, CurrentMonth);

        var savings = result.Buckets.Single(b => b.Bucket == "savings");
        Assert.Equal(1_500_000m, savings.Spent);
    }

    private static BudgetService Service(FinViet.Infrastructure.Persistence.Context.FinVietDbContext db)
        => new(db, new NoOpBudgetAlertNotifier(), new IncomeAllocationService(db), NullLogger<BudgetService>.Instance);

    private static Customer Customer(Guid id, decimal income)
        => new()
        {
            CustomerId = id,
            Email = $"{id}@finviet.local",
            FullName = "Test Customer",
            IsActive = true,
            MonthlyIncomeExpected = income,
            NeedsPct = 50,
            WantsPct = 30,
            SavingsPct = 20
        };

    private static Wallet Wallet(Guid id, Guid customerId)
        => new()
        {
            WalletId = id,
            CustomerId = customerId,
            WalletName = "Ví chính",
            WalletType = "basic",
            Balance = 100_000_000m,
            IsDeleted = false
        };

    private static Category Category(string id, string name, string? bucket)
        => new()
        {
            CategoryId = id,
            CategoryName = name,
            NameEn = name,
            Type = "expense",
            DefaultBucket = bucket,
            SortOrder = 0
        };

    private static Transaction Tx(
        Guid customerId, Guid walletId, string categoryId, string type, decimal amount, string date)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            CategoryId = categoryId,
            TransactionType = type,
            Amount = amount,
            TransactionDate = DateTime.Parse(date),
            EntryMethod = "manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private sealed class NoOpBudgetAlertNotifier : IBudgetAlertNotifier
    {
        public Task SendBudgetAlertAsync(
            Guid customerId, Guid budgetId, string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
