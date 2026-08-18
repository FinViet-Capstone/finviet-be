using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Application.UnitTests;

// HasData gate: a period with zero expense transactions must be flagged (HasData=false) and
// must not spend an LLM call narrating the neutral-50 baseline as if it were a real score.
// Spike scoping: the trailing-30-day window is a mean/std baseline only — spike days are
// counted solely inside [periodStart, periodEnd], so last month's spikes don't drag down the
// current week's/month's score.
public class SpendingScoreServiceTests
{
    // Fixed weekly period, decoupled from DateTime.UtcNow.
    private static readonly DateOnly PeriodStart = new(2026, 5, 11); // Monday
    private static readonly DateOnly PeriodEnd = new(2026, 5, 17);

    [Fact]
    public async Task Compute_NoExpenseInPeriod_ReturnsHasDataFalse_NeutralScore_NoComment()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        SeedBase(db, customerId, walletId);
        // Plenty of history, but all of it well before the period AND outside the 30-day
        // spike window's 7-distinct-day cold-start minimum.
        db.Transactions.Add(Tx(customerId, walletId, "expense", 500_000m, "2026-03-01"));
        await db.SaveChangesAsync();

        var ai = new RecordingAiClient();
        var result = await Service(db, ai).ComputeAsync(
            customerId, "WEEKLY", PeriodStart, PeriodEnd, persist: false, includeComment: true);

        Assert.False(result.HasData);
        Assert.Equal(50m, result.FinalScore); // neutral baseline, hidden behind HasData on the FE
        Assert.Null(result.Comment);
        Assert.Equal(0, ai.CommentCalls); // no LLM call for an empty period
    }

    [Fact]
    public async Task Compute_ExpenseInsidePeriod_ReturnsHasDataTrue_AndGeneratesComment()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        SeedBase(db, customerId, walletId);
        db.Transactions.Add(Tx(customerId, walletId, "expense", 120_000m, "2026-05-12"));
        await db.SaveChangesAsync();

        var ai = new RecordingAiClient();
        var result = await Service(db, ai).ComputeAsync(
            customerId, "WEEKLY", PeriodStart, PeriodEnd, persist: false, includeComment: true);

        Assert.True(result.HasData);
        Assert.Equal("AI comment", result.Comment);
        Assert.Equal(1, ai.CommentCalls);
    }

    [Fact]
    public async Task Compute_IncomeOnlyInsidePeriod_StillHasDataFalse()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        SeedBase(db, customerId, walletId);
        db.Transactions.Add(Tx(customerId, walletId, "income", 10_000_000m, "2026-05-12"));
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingAiClient()).ComputeAsync(
            customerId, "WEEKLY", PeriodStart, PeriodEnd, persist: false, includeComment: true);

        Assert.False(result.HasData); // the score assesses spending, and none happened
    }

    [Fact]
    public async Task Spike_OutsidePeriod_DoesNotPenalizeCurrentPeriod()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        SeedBase(db, customerId, walletId);
        // Baseline: 8 flat days in the trailing window before the period (satisfies the
        // 7-distinct-day cold start), plus one huge spike day BEFORE periodStart.
        for (var d = 0; d < 8; d++)
            db.Transactions.Add(Tx(customerId, walletId, "expense", 100_000m, $"2026-04-{20 + d:00}"));
        db.Transactions.Add(Tx(customerId, walletId, "expense", 5_000_000m, "2026-05-01"));
        // In-period spending is ordinary.
        db.Transactions.Add(Tx(customerId, walletId, "expense", 100_000m, "2026-05-12"));
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingAiClient()).ComputeAsync(
            customerId, "WEEKLY", PeriodStart, PeriodEnd, persist: false, includeComment: false);

        Assert.True(result.HasData);
        Assert.Equal(100m, result.SpikeScore); // 05-01 spike is baseline history, not this week's fault
    }

    [Fact]
    public async Task Spike_InsidePeriod_IsPenalized()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        SeedBase(db, customerId, walletId);
        for (var d = 0; d < 8; d++)
            db.Transactions.Add(Tx(customerId, walletId, "expense", 100_000m, $"2026-04-{20 + d:00}"));
        db.Transactions.Add(Tx(customerId, walletId, "expense", 5_000_000m, "2026-05-12")); // spike inside the period
        await db.SaveChangesAsync();

        var result = await Service(db, new RecordingAiClient()).ComputeAsync(
            customerId, "WEEKLY", PeriodStart, PeriodEnd, persist: false, includeComment: false);

        Assert.NotNull(result.SpikeScore);
        Assert.True(result.SpikeScore < 100m);
    }

    private static void SeedBase(FinVietDbContext db, Guid customerId, Guid walletId)
    {
        db.Customers.Add(new Customer
        {
            CustomerId = customerId,
            Email = $"{customerId}@finviet.local",
            FullName = "Test Customer",
            IsActive = true,
            MonthlyIncomeExpected = 10_000_000m,
            NeedsPct = 50,
            WantsPct = 30,
            SavingsPct = 20
        });
        db.Wallets.Add(new Wallet
        {
            WalletId = walletId,
            CustomerId = customerId,
            WalletName = "Ví chính",
            WalletType = "basic",
            Balance = 100_000_000m,
            IsDeleted = false
        });
        db.ScoringCriteria.AddRange(
            Criterion("spike", weekly: 50m, monthly: 30m),
            Criterion("budget", weekly: 50m, monthly: 40m),
            Criterion("savings", weekly: 0m, monthly: 30m));
    }

    private static ScoringCriterion Criterion(string code, decimal weekly, decimal monthly)
        => new()
        {
            CriterionId = Guid.NewGuid(),
            Code = code,
            CriterionName = code,
            WeightWeekly = weekly,
            WeightMonthly = monthly,
            Version = 1
        };

    private static Transaction Tx(Guid customerId, Guid walletId, string type, decimal amount, string date)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            CategoryId = "cat_food",
            TransactionType = type,
            Amount = amount,
            TransactionDate = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc),
            EntryMethod = "manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static SpendingScoreService Service(FinVietDbContext db, IAiModelClient ai)
        => new(db, ai, NullLogger<SpendingScoreService>.Instance);

    private sealed class RecordingAiClient : IAiModelClient
    {
        public int CommentCalls { get; private set; }

        public Task<string> GenerateScoreCommentAsync(
            string scoreContext, CancellationToken cancellationToken = default, AiRequestContext? requestContext = null)
        {
            CommentCalls++;
            return Task.FromResult("AI comment");
        }

        public Task<AiClassificationResult> ClassifyAsync(
            string input, IReadOnlyList<string> allowedCategories,
            CancellationToken cancellationToken = default, AiRequestContext? requestContext = null)
            => throw new NotSupportedException();

        public Task<string> GenerateReportAsync(
            string reportContext, CancellationToken cancellationToken = default, AiRequestContext? requestContext = null)
            => throw new NotSupportedException();

        public Task<string> ChatAsync(
            string contextBlock, IReadOnlyList<AiChatTurn> recentTurns, string question,
            CancellationToken cancellationToken = default, AiRequestContext? requestContext = null)
            => throw new NotSupportedException();
    }
}
