using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Moq;

namespace FinViet.Application.UnitTests;

public class FinancialContextServiceTests
{
    [Fact]
    public async Task BuildCurrentMonthAsync_ComputesPositiveBudgetOverrun()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var today = DateTime.UtcNow.AddHours(7);
        db.Customers.Add(Customer(customerId));
        db.Wallets.Add(new Wallet
        {
            WalletId = walletId,
            CustomerId = customerId,
            WalletName = "Ví chính",
            WalletType = "basic",
            Balance = 2_000_000m
        });
        db.Categories.Add(new Category
        {
            CategoryId = "cat_food",
            CategoryName = "Ăn uống",
            Type = "expense"
        });
        db.Budgets.Add(new Budget
        {
            BudgetId = Guid.NewGuid(),
            CustomerId = customerId,
            CategoryId = "cat_food",
            MonthlyLimit = 1_000_000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Transactions.Add(new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            CategoryId = "cat_food",
            Amount = 1_250_000m,
            TransactionType = "expense",
            EntryMethod = "manual",
            TransactionDate = DateTime.SpecifyKind(
                new DateTime(today.Year, today.Month, Math.Max(1, today.Day - 1)),
                DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new FinancialContextService(db, ScoreService().Object);

        var result = await service.BuildCurrentMonthAsync(customerId);

        Assert.Contains("Ngân sách Ăn uống", result.Content);
        Assert.Contains("vượt 250,000đ", result.Content);
        Assert.Contains(result.Citations, c => c.SourceType == "budget_summary");
    }

    [Fact]
    public async Task BuildCurrentMonthAsync_DisabledScopes_DoNotQueryOrEmitPrivateFacts()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(Customer(customerId));
        db.AiCustomerPreferences.Add(new AiCustomerPreference
        {
            CustomerId = customerId,
            ShareBalances = false,
            ShareTransactions = false,
            ShareBudgets = false,
            ShareGoals = false,
            ShareReports = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var score = new Mock<ISpendingScoreService>(MockBehavior.Strict);
        var service = new FinancialContextService(db, score.Object);

        var result = await service.BuildCurrentMonthAsync(customerId);

        Assert.Contains("Phạm vi số dư đã bị khách hàng tắt", result.Content);
        Assert.Contains("Phạm vi giao dịch đã bị khách hàng tắt", result.Content);
        Assert.Contains("Phạm vi ngân sách đã bị khách hàng tắt", result.Content);
        Assert.Contains("Phạm vi mục tiêu tiết kiệm đã bị khách hàng tắt", result.Content);
        Assert.Contains("Phạm vi báo cáo AI đã bị khách hàng tắt", result.Content);
        Assert.DoesNotContain(result.Citations, c =>
            c.SourceType is "wallet_summary" or "transaction_summary" or "budget_summary"
                or "saving_goal_summary" or "weekly_report");
        score.VerifyNoOtherCalls();
    }

    private static Mock<ISpendingScoreService> ScoreService()
    {
        var score = new Mock<ISpendingScoreService>(MockBehavior.Strict);
        score.Setup(x => x.ComputeCurrentAsync(
                It.IsAny<Guid>(),
                "MONTHLY",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendingScoreResult
            {
                PeriodType = "MONTHLY",
                FinalScore = 75m,
                ColorBadge = "YELLOW"
            });
        return score;
    }

    private static Customer Customer(Guid customerId) => new()
    {
        CustomerId = customerId,
        Email = $"{customerId:N}@finviet.local",
        FullName = "Context Customer",
        IsActive = true,
        MonthlyIncomeExpected = 10_000_000m
    };
}
