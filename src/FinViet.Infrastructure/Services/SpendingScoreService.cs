using System.Text.Json;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Computes the "Chấm Điểm Ví" spending score (0–100) from live transaction data.
/// Weekly = Spike×50 + Budget×50; Monthly = Spike×30 + Budget×40 + Savings×30.
/// Missing metrics (null) are dropped and remaining weights re-normalized.
/// </summary>
public class SpendingScoreService : ISpendingScoreService
{
    private const decimal SpikeThresholdVnd = 200_000m;
    private const double SpikeZThreshold = 2.0;
    private const string Uncategorized = "Chưa phân loại";

    private readonly FinVietDbContext _db;
    private readonly IGeminiClient _gemini;
    private readonly ILogger<SpendingScoreService> _logger;

    public SpendingScoreService(FinVietDbContext db, IGeminiClient gemini, ILogger<SpendingScoreService> logger)
    {
        _db = db;
        _gemini = gemini;
        _logger = logger;
    }

    public Task<SpendingScoreResult> ComputeCurrentAsync(
        Guid customerId, string periodType, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (start, end) = periodType.ToUpperInvariant() switch
        {
            "MONTHLY" => (new DateOnly(today.Year, today.Month, 1), today),
            _ => (StartOfWeek(today), today)
        };
        return ComputeAsync(customerId, periodType, start, end, persist: false, includeComment: true, cancellationToken);
    }

    public async Task<SpendingScoreResult> ComputeAsync(
        Guid customerId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        bool persist, bool includeComment, CancellationToken cancellationToken = default)
    {
        var type = periodType.ToUpperInvariant();
        var isMonthly = type == "MONTHLY";

        var spike = await ComputeSpikeAsync(customerId, periodEnd, cancellationToken);
        var budget = await ComputeBudgetAsync(customerId, periodStart, periodEnd, cancellationToken);
        decimal? savings = isMonthly
            ? await ComputeSavingsAsync(customerId, periodEnd, cancellationToken)
            : null;

        // Base weights per view.
        var baseWeights = isMonthly
            ? new Dictionary<string, decimal> { ["spike"] = 30m, ["budget"] = 40m, ["savings"] = 30m }
            : new Dictionary<string, decimal> { ["spike"] = 50m, ["budget"] = 50m };

        var present = new Dictionary<string, (decimal score, decimal weight)>();
        if (spike is not null) present["spike"] = (spike.Value, baseWeights["spike"]);
        if (budget is not null) present["budget"] = (budget.Value, baseWeights["budget"]);
        if (isMonthly && savings is not null) present["savings"] = (savings.Value, baseWeights["savings"]);

        decimal finalScore;
        var effectiveWeights = new Dictionary<string, decimal>();

        if (present.Count == 0)
        {
            // No metric had data — neutral baseline rather than 0.
            finalScore = 50m;
        }
        else
        {
            var totalWeight = present.Values.Sum(v => v.weight);
            finalScore = present.Values.Sum(v => v.score * (v.weight / totalWeight));
            foreach (var kv in present)
                effectiveWeights[kv.Key] = Math.Round(kv.Value.weight / totalWeight * 100m, 2);
        }

        finalScore = Math.Round(Math.Clamp(finalScore, 0m, 100m), 2);
        var badge = finalScore >= 80m ? "GREEN" : finalScore >= 50m ? "YELLOW" : "RED";

        var result = new SpendingScoreResult
        {
            PeriodType = type,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FinalScore = finalScore,
            SpikeScore = spike,
            BudgetScore = budget,
            SavingsScore = savings,
            Weights = effectiveWeights,
            ColorBadge = badge
        };

        if (includeComment)
            result.Comment = await TryGenerateCommentAsync(result, cancellationToken);

        if (persist)
            await SnapshotAsync(customerId, result, cancellationToken);

        return result;
    }

    // ── Metric 1: Spike (Z-score over trailing 30 days) ──────────────────────────────
    private async Task<decimal?> ComputeSpikeAsync(Guid customerId, DateOnly asOf, CancellationToken ct)
    {
        var windowStart = asOf.AddDays(-30);

        var rows = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.t.TransactionType == "expense"
                        && x.t.TransactionDate != null
                        && x.t.TransactionDate >= DateRange.StartUtc(windowStart)
                        && x.t.TransactionDate <= DateRange.EndUtc(asOf))
            .Select(x => new { x.t.TransactionDate, x.t.Amount })
            .ToListAsync(ct);

        // Cold start: need at least 7 distinct days of data.
        var distinctDays = rows.Select(r => DateOnly.FromDateTime(r.TransactionDate!.Value)).Distinct().Count();
        if (distinctDays < 7)
            return null;

        var dailyTotals = rows
            .GroupBy(r => DateOnly.FromDateTime(r.TransactionDate!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        if (dailyTotals.Count == 0)
            return 100m;

        var values = dailyTotals.Values.Select(v => (double)v).ToList();
        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        var std = Math.Sqrt(variance);

        if (std < 1e-6)
            return 100m; // perfectly flat spending → no spikes

        // Count spike days and accumulate severity.
        var spikeDays = 0;
        double severitySum = 0;
        foreach (var (_, amount) in dailyTotals)
        {
            var z = ((double)amount - mean) / std;
            if (z > SpikeZThreshold && amount > SpikeThresholdVnd)
            {
                spikeDays++;
                severitySum += z - SpikeZThreshold; // how far past the threshold
            }
        }

        if (spikeDays == 0)
            return 100m;

        // Penalty: 15 points per spike day + severity scaling, capped so score stays >= 0.
        var penalty = spikeDays * 15.0 + severitySum * 5.0;
        return (decimal)Math.Max(0.0, 100.0 - penalty);
    }

    // ── Metric 2: Budget Adherence (pacing) ──────────────────────────────────────────
    private async Task<decimal?> ComputeBudgetAsync(Guid customerId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // Flat recurring budgets: one row per (category[, wallet]) with a monthly limit.
        var budgets = await _db.Budgets
            .Where(b => b.CustomerId == customerId)
            .Select(b => new { b.CategoryId, b.MonthlyLimit })
            .ToListAsync(ct);
        if (budgets.Count == 0)
            return null;

        // Effective bucket per category: customer override (customer_categories) over category.default_bucket.
        var userBuckets = await _db.CustomerCategories
            .Where(u => u.CustomerId == customerId && u.IsActive)
            .ToDictionaryAsync(u => u.CategoryId, u => u.BucketId, ct);

        var categories = await _db.Categories
            .Where(c => c.Type == "expense")
            .Select(c => new { c.CategoryId, c.CategoryName, c.DefaultBucket })
            .ToListAsync(ct);
        var catInfo = categories.ToDictionary(c => c.CategoryId, c => c);

        string? BucketOf(string categoryId) =>
            userBuckets.TryGetValue(categoryId, out var b) ? b
            : catInfo.TryGetValue(categoryId, out var c) ? c.DefaultBucket : null;

        // Actual spend per category (recomputed from transactions; exclude Uncategorized).
        var actuals = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.t.TransactionType == "expense"
                        && x.t.CategoryId != null
                        && x.t.TransactionDate != null
                        && x.t.TransactionDate >= DateRange.StartUtc(start)
                        && x.t.TransactionDate <= DateRange.EndUtc(end))
            .GroupBy(x => x.t.CategoryId!)
            .Select(g => new { CategoryId = g.Key, Spent = g.Sum(x => x.t.Amount) })
            .ToListAsync(ct);
        var actualByCat = actuals.ToDictionary(a => a.CategoryId, a => a.Spent);

        var totalDays = (end.DayNumber - start.DayNumber) + 1;
        var elapsedDays = totalDays; // computing over a closed/elapsed window
        var pacingFraction = totalDays > 0 ? (decimal)elapsedDays / totalDays : 1m;

        // Aggregate per bucket.
        var bucketLimit = new Dictionary<string, decimal>();
        var bucketActual = new Dictionary<string, decimal>();
        foreach (var b in budgets)
        {
            var bucket = b.CategoryId is null ? null : BucketOf(b.CategoryId);
            if (bucket is null ||
                (catInfo.TryGetValue(b.CategoryId!, out var info) && info.CategoryName == Uncategorized))
                continue;

            var key = NormalizeBucket(bucket);
            bucketLimit[key] = bucketLimit.GetValueOrDefault(key) + b.MonthlyLimit;
            var spent = actualByCat.GetValueOrDefault(b.CategoryId!);
            bucketActual[key] = bucketActual.GetValueOrDefault(key) + spent;
        }

        if (bucketLimit.Count == 0)
            return null;

        // Per-bucket pacing score, Needs weighted higher than Wants.
        var bucketWeights = new Dictionary<string, decimal> { ["NEEDS"] = 0.6m, ["WANTS"] = 0.4m, ["SAVINGS"] = 0.0m };
        decimal weightedSum = 0m, weightTotal = 0m;
        foreach (var (bucket, limit) in bucketLimit)
        {
            if (limit <= 0m) continue;
            var expected = limit * pacingFraction;
            var actual = bucketActual.GetValueOrDefault(bucket);
            var score = PacingScore(actual, expected);
            var w = bucketWeights.GetValueOrDefault(bucket, 0.4m);
            if (w <= 0m) continue;
            weightedSum += score * w;
            weightTotal += w;
        }

        return weightTotal > 0m ? Math.Round(weightedSum / weightTotal, 2) : (decimal?)null;
    }

    // Normalize a bucket id / expense_class to the canonical NEEDS/WANTS/SAVINGS key.
    private static string NormalizeBucket(string? bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket))
            return "UNASSIGNED";

        return bucket.Trim().ToUpperInvariant() switch
        {
            "NEED" or "NEEDS" => "NEEDS",
            "WANT" or "WANTS" => "WANTS",
            "SAVING" or "SAVINGS" => "SAVINGS",
            _ => "UNASSIGNED"
        };
    }

    private static decimal PacingScore(decimal actual, decimal expected)
    {
        if (expected <= 0m)
            return actual <= 0m ? 100m : 0m;
        if (actual <= expected)
            return 100m;
        var deviation = (actual - expected) / expected; // >0 overspend
        // 20% over → 80, 50% over → 50, 100% over → 0 (linear, clamped).
        var score = 100m - deviation * 100m;
        return Math.Clamp(score, 0m, 100m);
    }

    // ── Metric 3: Savings Consistency (CV over 3–6 months) ───────────────────────────
    private async Task<decimal?> ComputeSavingsAsync(Guid customerId, DateOnly asOf, CancellationToken ct)
    {
        var income = await _db.Customers
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.MonthlyIncomeExpected)
            .FirstOrDefaultAsync(ct);
        if (income is null or <= 0m)
            return null;

        // Look back up to 6 full months (excluding current in-progress month boundary handling kept simple).
        var monthsBack = 6;
        var firstMonth = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-(monthsBack - 1));
        var windowStart = DateRange.StartUtc(firstMonth);
        var windowEnd = DateRange.EndUtc(asOf);

        // Savings-bucket transactions (categories with expense_class = SAVINGS).
        var savingsCatIds = await _db.Categories
            .Where(c => c.DefaultBucket == "savings")
            .Select(c => c.CategoryId)
            .ToListAsync(ct);

        var savingsTxns = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.t.TransactionType == "expense"
                        && x.t.CategoryId != null
                        && savingsCatIds.Contains(x.t.CategoryId!)
                        && x.t.TransactionDate >= windowStart
                        && x.t.TransactionDate <= windowEnd)
            .Select(x => new { x.t.TransactionDate, x.t.Amount })
            .ToListAsync(ct);

        // Saving goal contributions (separate from transactions; scoped via goal→customer).
        var contributions = await _db.SavingGoalContributions
            .Join(_db.SavingGoals, sc => sc.GoalId, g => g.GoalId, (sc, g) => new { sc, g.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.sc.ContributionDate >= windowStart
                        && x.sc.ContributionDate <= windowEnd)
            .Select(x => new { Date = x.sc.ContributionDate, x.sc.Amount })
            .ToListAsync(ct);

        // Sum saved per calendar month (combined).
        var perMonth = new Dictionary<DateOnly, decimal>();
        void Add(DateTime? d, decimal amt)
        {
            if (d is null) return;
            var key = new DateOnly(d.Value.Year, d.Value.Month, 1);
            perMonth[key] = perMonth.GetValueOrDefault(key) + amt;
        }
        foreach (var s in savingsTxns) Add(s.TransactionDate, s.Amount);
        foreach (var c in contributions) Add(c.Date, c.Amount);

        if (perMonth.Count < 3)
            return null; // need at least 3 months of history

        var rates = perMonth.Values.Select(saved => (double)(saved / income.Value)).ToList();
        var mu = rates.Average();
        if (mu <= 0)
            return 0m;

        var sigma = Math.Sqrt(rates.Sum(r => Math.Pow(r - mu, 2)) / rates.Count);
        var cv = sigma / mu;

        // Target attainment (assume 20% target) + consistency (low CV).
        const double targetRate = 0.20;
        var attainment = Math.Clamp(mu / targetRate, 0.0, 1.0);      // 0..1
        var consistency = Math.Clamp(1.0 - cv, 0.0, 1.0);            // 0..1
        var score = (attainment * 0.6 + consistency * 0.4) * 100.0;
        return (decimal)Math.Round(score, 2);
    }

    private async Task<string?> TryGenerateCommentAsync(SpendingScoreResult r, CancellationToken ct)
    {
        try
        {
            var ctx =
                $"Điểm tổng: {r.FinalScore}/100 (xếp loại {r.ColorBadge}). " +
                $"Spike: {Fmt(r.SpikeScore)}, Budget: {Fmt(r.BudgetScore)}, Savings: {Fmt(r.SavingsScore)}. " +
                $"Kỳ: {r.PeriodType}.";
            return await _gemini.GenerateScoreCommentAsync(ctx, ct);
        }
        catch (GeminiUnavailableException ex)
        {
            _logger.LogWarning(ex, "Score comment generation skipped (Gemini unavailable).");
            return null;
        }

        static string Fmt(decimal? v) => v is null ? "không đủ dữ liệu" : v.Value.ToString("0.#");
    }

    private async Task SnapshotAsync(Guid customerId, SpendingScoreResult r, CancellationToken ct)
    {
        var existing = await _db.AiSpendingScores.FirstOrDefaultAsync(
            s => s.CustomerId == customerId && s.PeriodType == r.PeriodType && s.PeriodStart == r.PeriodStart, ct);

        var weightsJson = JsonSerializer.Serialize(r.Weights);

        if (existing is null)
        {
            _db.AiSpendingScores.Add(new AiSpendingScore
            {
                ScoreId = Guid.NewGuid(),
                CustomerId = customerId,
                PeriodType = r.PeriodType,
                PeriodStart = r.PeriodStart,
                PeriodEnd = r.PeriodEnd,
                FinalScore = r.FinalScore,
                SpikeScore = r.SpikeScore,
                BudgetScore = r.BudgetScore,
                SavingsScore = r.SavingsScore,
                WeightsJson = weightsJson,
                ColorBadge = r.ColorBadge,
                Comment = r.Comment,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        // If a snapshot already exists for this closed period, keep it (idempotent re-run).
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        // Monday-based week.
        int diff = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-diff);
    }
}
