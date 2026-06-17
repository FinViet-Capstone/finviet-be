using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

// Flat recurring budgets (schema v2.1 §5 / BUSINESS_LOGIC §6/§9/§10):
//  - 1 dòng/(customer, category, wallet), monthly_limit; spent tính ĐỘNG theo tháng (ICT).
//  - % hũ lấy từ customer; mẫu số bucket = allocationCap = income × pct (KHÔNG Σ category limits).
//  - Alert dedup theo last_alert_threshold + last_alert_month (reset mỗi tháng ICT).
public class BudgetService : IBudgetService
{
    private const string UncategorizedName = "Chưa phân loại";
    private const decimal WarningThreshold = 80m;
    private const decimal ExceededThreshold = 100m;

    // Mọi biên tháng tính theo Asia/Ho_Chi_Minh (UTC+7).
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    private readonly FinVietDbContext _dbContext;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly ILogger<BudgetService> _logger;

    public BudgetService(
        FinVietDbContext dbContext,
        IBudgetAlertNotifier budgetAlertNotifier,
        ILogger<BudgetService> logger)
    {
        _dbContext = dbContext;
        _budgetAlertNotifier = budgetAlertNotifier;
        _logger = logger;
    }

    // ── GET /budgets?month ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<BudgetResponse>> GetBudgetsAsync(
        Guid customerId,
        string? month,
        CancellationToken cancellationToken = default)
    {
        var window = ResolveMonth(month);

        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var categoryIds = budgets.Select(b => b.CategoryId).Distinct().ToList();
        var spentByScope = await ComputeScopedSpentAsync(customerId, window, categoryIds, cancellationToken);

        return budgets
            .Select(b => BuildBudgetResponse(b, spentByScope, window))
            .OrderByDescending(x => x.Percentage)
            .ToList();
    }

    // ── GET /budgets/buckets?month ────────────────────────────────────────────
    public async Task<BucketSummaryListResponse> GetBucketSummaryAsync(
        Guid customerId,
        string? month,
        CancellationToken cancellationToken = default)
    {
        var window = ResolveMonth(month);

        var customer = await _dbContext.Customers
            .Where(c => c.CustomerId == customerId)
            .Select(c => new { c.MonthlyIncomeExpected, c.NeedsPct, c.WantsPct, c.SavingsPct })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
            throw new NotFoundException("Customer not found.");

        if (customer.MonthlyIncomeExpected is null or <= 0)
            throw new ValidationException(
                "Monthly income is required for the 50-30-20 view. Please set it in onboarding.");

        var income = customer.MonthlyIncomeExpected.Value;

        var (buckets, uncategorizedRatio) = await BuildBucketsAsync(
            customerId, income, customer.NeedsPct, customer.WantsPct, customer.SavingsPct, window, cancellationToken);

        return new BucketSummaryListResponse
        {
            Month = window.Key,
            MonthlyIncome = income,
            NeedsPct = customer.NeedsPct,
            WantsPct = customer.WantsPct,
            SavingsPct = customer.SavingsPct,
            Buckets = buckets,
            BudgetAdherenceScore = CalculateBudgetAdherenceScore(buckets),
            UncategorizedRatio = uncategorizedRatio,
            UncategorizedWarning = uncategorizedRatio > 20m
        };
    }

    // ── POST /budgets (upsert) ────────────────────────────────────────────────
    public async Task<BudgetResponse> UpsertBudgetAsync(
        Guid customerId,
        UpsertBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MonthlyLimit <= 0)
            throw new ValidationException("Monthly limit must be greater than 0.");

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == request.CategoryId, cancellationToken);

        if (category is null)
            throw new NotFoundException("Category not found.");

        if (!string.Equals(category.Type, "EXPENSE", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Budget categories must be expense categories.");

        if (string.Equals(category.CategoryName, UncategorizedName, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Uncategorized transactions cannot be used as a budget category.");

        if (request.WalletId.HasValue)
        {
            var ownsWallet = await _dbContext.Wallets
                .AnyAsync(w => w.WalletId == request.WalletId.Value && w.CustomerId == customerId && !w.IsDeleted, cancellationToken);

            if (!ownsWallet)
                throw new ValidationException("Wallet does not belong to this customer.");
        }

        // Upsert theo (customer, category, wallet). Tách null để EF dịch đúng "wallet_id IS NULL".
        var existing = request.WalletId.HasValue
            ? await _dbContext.Budgets.FirstOrDefaultAsync(
                b => b.CustomerId == customerId && b.CategoryId == request.CategoryId && b.WalletId == request.WalletId.Value,
                cancellationToken)
            : await _dbContext.Budgets.FirstOrDefaultAsync(
                b => b.CustomerId == customerId && b.CategoryId == request.CategoryId && b.WalletId == null,
                cancellationToken);

        Budget budget;
        if (existing is not null)
        {
            existing.MonthlyLimit = request.MonthlyLimit;
            existing.LastAlertThreshold = 0m; // đổi hạn mức → đánh giá lại mốc alert
            existing.UpdatedAt = DateTime.UtcNow;
            budget = existing;
        }
        else
        {
            budget = new Budget
            {
                BudgetId = Guid.NewGuid(),
                CustomerId = customerId,
                CategoryId = request.CategoryId,
                WalletId = request.WalletId,
                MonthlyLimit = request.MonthlyLimit,
                LastAlertThreshold = 0m,
                LastAlertMonth = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Budgets.Add(budget);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        budget.Category = category;
        return await BuildSingleBudgetResponseAsync(customerId, budget, cancellationToken);
    }

    // ── PATCH /budgets/{id} ───────────────────────────────────────────────────
    public async Task<BudgetResponse> UpdateBudgetAsync(
        Guid customerId,
        Guid budgetId,
        UpdateBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MonthlyLimit <= 0)
            throw new ValidationException("Monthly limit must be greater than 0.");

        var budget = await _dbContext.Budgets
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.CustomerId == customerId, cancellationToken);

        if (budget is null)
            throw new NotFoundException("Budget not found.");

        budget.MonthlyLimit = request.MonthlyLimit;
        budget.LastAlertThreshold = 0m;
        budget.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildSingleBudgetResponseAsync(customerId, budget, cancellationToken);
    }

    // ── DELETE /budgets/{id} ──────────────────────────────────────────────────
    public async Task<bool> DeleteBudgetAsync(
        Guid customerId,
        Guid budgetId,
        CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Budgets
            .FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.CustomerId == customerId, cancellationToken);

        if (budget is null)
            throw new NotFoundException("Budget not found.");

        _dbContext.Budgets.Remove(budget);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    // ── Budget Adherence (Metric 2 của Spending Score) ────────────────────────
    public async Task<decimal?> ComputeBudgetAdherenceScoreAsync(
        Guid customerId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var customer = await _dbContext.Customers
            .Where(c => c.CustomerId == customerId)
            .Select(c => new { c.MonthlyIncomeExpected, c.NeedsPct, c.WantsPct, c.SavingsPct })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null || customer.MonthlyIncomeExpected is null or <= 0)
            return null;

        // Budget Adherence là metric theo THÁNG → dùng tháng chứa periodEnd.
        var window = ResolveMonthFromDate(periodEnd);

        var (buckets, _) = await BuildBucketsAsync(
            customerId, customer.MonthlyIncomeExpected.Value,
            customer.NeedsPct, customer.WantsPct, customer.SavingsPct, window, cancellationToken);

        return CalculateBudgetAdherenceScore(buckets);
    }

    // ── Alert sau khi transaction thay đổi (BL §2b) ───────────────────────────
    // Luôn đánh giá theo THÁNG HIỆN TẠI (ICT) vì alert "vượt 80/100" là real-time.
    // Không bao giờ ném ra ngoài để không làm hỏng thao tác transaction đã commit.
    public async Task SyncBudgetOnTransactionChangeAsync(
        Guid customerId,
        DateOnly affectedDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var window = ResolveMonth(null); // tháng hiện tại ICT

            var budgets = await _dbContext.Budgets
                .Include(b => b.Category)
                .Where(b => b.CustomerId == customerId)
                .ToListAsync(cancellationToken);

            if (budgets.Count == 0)
                return;

            var categoryIds = budgets.Select(b => b.CategoryId).Distinct().ToList();
            var spentByScope = await ComputeScopedSpentAsync(customerId, window, categoryIds, cancellationToken);

            var pendingAlerts = new List<BudgetAlertPayload>();

            foreach (var budget in budgets)
            {
                // Sang tháng mới (ICT) → reset cờ alert.
                if (budget.LastAlertMonth != window.Key)
                {
                    budget.LastAlertThreshold = 0m;
                    budget.LastAlertMonth = window.Key;
                }

                var spent = ScopedSpentFor(budget, spentByScope);
                var usedPercentage = CalculatePercentage(spent, budget.MonthlyLimit);

                var crossedThreshold = usedPercentage >= ExceededThreshold
                    ? ExceededThreshold
                    : usedPercentage >= WarningThreshold
                        ? WarningThreshold
                        : 0m;

                if (crossedThreshold > budget.LastAlertThreshold)
                {
                    var alert = CreateBudgetAlert(customerId, budget, usedPercentage, spent, crossedThreshold);
                    pendingAlerts.Add(alert);
                    _dbContext.Notifications.Add(alert.Notification);
                    budget.LastAlertThreshold = crossedThreshold;
                }
                else if (usedPercentage < WarningThreshold && budget.LastAlertThreshold > 0m)
                {
                    budget.LastAlertThreshold = 0m;
                }

                budget.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var alert in pendingAlerts)
            {
                await _budgetAlertNotifier.SendBudgetAlertAsync(
                    customerId, alert.Title, alert.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to sync budget after transaction change for customer {CustomerId}.", customerId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BudgetResponse> BuildSingleBudgetResponseAsync(
        Guid customerId, Budget budget, CancellationToken cancellationToken)
    {
        var window = ResolveMonth(null);
        var spent = await ComputeScopedSpentAsync(customerId, window, new[] { budget.CategoryId }, cancellationToken);
        return BuildBudgetResponse(budget, spent, window);
    }

    private static BudgetResponse BuildBudgetResponse(Budget budget, IReadOnlyList<ScopedSpent> spentByScope, MonthWindow window)
    {
        var spent = ScopedSpentFor(budget, spentByScope);
        var percentage = CalculatePercentage(spent, budget.MonthlyLimit);
        var expected = CalculateExpected(budget.MonthlyLimit, window);
        var deviation = CalculatePaceDeviation(spent, expected);

        return new BudgetResponse
        {
            Id = budget.BudgetId,
            CategoryId = budget.CategoryId,
            CategoryName = budget.Category?.CategoryName ?? string.Empty,
            WalletId = budget.WalletId,
            MonthlyLimit = budget.MonthlyLimit,
            Spent = spent,
            Remaining = budget.MonthlyLimit - spent,
            Percentage = percentage,
            Status = GetStatus(percentage),
            ExpectedSpent = expected,
            PaceDeviation = deviation,
            PaceStatus = GetPaceStatus(deviation)
        };
    }

    private static decimal ScopedSpentFor(Budget budget, IReadOnlyList<ScopedSpent> spentByScope)
        => budget.WalletId.HasValue
            ? spentByScope.Where(s => s.CategoryId == budget.CategoryId && s.WalletId == budget.WalletId.Value).Sum(s => s.Total)
            : spentByScope.Where(s => s.CategoryId == budget.CategoryId).Sum(s => s.Total);

    // Tổng chi theo (category, wallet) trong tháng — chỉ EXPENSE, loại "Chưa phân loại"
    // (transfer là TRANSFER nên cũng tự bị loại).
    private async Task<List<ScopedSpent>> ComputeScopedSpentAsync(
        Guid customerId, MonthWindow window, IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
            return new List<ScopedSpent>();

        var uncategorizedId = await GetUncategorizedIdAsync(cancellationToken);
        var ids = categoryIds.ToList();

        return await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            where transaction.CategoryId.HasValue
                  && ids.Contains(transaction.CategoryId.Value)
                  && (uncategorizedId == null || transaction.CategoryId.Value != uncategorizedId.Value)
                  && transaction.TransactionType == "EXPENSE"
                  && transaction.TransactionDate >= window.StartUtc
                  && transaction.TransactionDate < window.EndExclusiveUtc
                  && wallet.CustomerId == customerId
            group transaction by new { CategoryId = transaction.CategoryId!.Value, transaction.WalletId } into grouped
            select new ScopedSpent
            {
                CategoryId = grouped.Key.CategoryId,
                WalletId = grouped.Key.WalletId,
                Total = grouped.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);
    }

    // Dựng 3 hũ Needs/Wants/Savings: cap = income × pct, spent = mọi chi của hũ (loại uncategorized),
    // kèm pacing và cờ "vượt phân bổ" khi Σ category limits > cap (BL §6).
    private async Task<(List<BucketSummaryResponse> Buckets, decimal UncategorizedRatio)> BuildBucketsAsync(
        Guid customerId, decimal income, int needsPct, int wantsPct, int savingsPct, MonthWindow window, CancellationToken cancellationToken)
    {
        var uncategorizedId = await GetUncategorizedIdAsync(cancellationToken);

        var userBuckets = await _dbContext.UserCategoryBuckets
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .ToDictionaryAsync(x => x.CategoryId, x => x.Bucket, cancellationToken);

        // Mọi chi tiêu trong tháng + thông tin hũ mặc định (ExpenseClass) của category.
        var spentRows = await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            join category in _dbContext.Categories.AsNoTracking()
                on transaction.CategoryId equals category.CategoryId
            where wallet.CustomerId == customerId
                  && transaction.CategoryId != null
                  && transaction.TransactionType == "EXPENSE"
                  && transaction.TransactionDate >= window.StartUtc
                  && transaction.TransactionDate < window.EndExclusiveUtc
            select new
            {
                transaction.Amount,
                transaction.CategoryId,
                category.CategoryName,
                category.ExpenseClass
            })
            .ToListAsync(cancellationToken);

        var totalAllSpent = spentRows.Sum(x => x.Amount);
        var uncategorizedSpent = uncategorizedId is null
            ? 0m
            : spentRows.Where(x => x.CategoryId == uncategorizedId.Value).Sum(x => x.Amount);

        var uncategorizedRatio = totalAllSpent > 0
            ? Math.Round(uncategorizedSpent / totalAllSpent * 100m, 2)
            : 0m;

        // Hạn mức category theo hũ (để tính cờ over-allocation).
        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var allocations = new (string Bucket, int Pct)[]
        {
            ("NEEDS", needsPct),
            ("WANTS", wantsPct),
            ("SAVINGS", savingsPct)
        };

        var buckets = new List<BucketSummaryResponse>();

        foreach (var (bucket, pct) in allocations)
        {
            var cap = Math.Round(income * pct / 100m, 2);

            var rows = spentRows
                .Where(x => NormalizeBucket(ResolveEffectiveBucket(x.CategoryId, x.ExpenseClass, userBuckets)) == bucket
                            && (uncategorizedId is null || x.CategoryId != uncategorizedId.Value))
                .ToList();

            var spent = rows.Sum(x => x.Amount);
            var usedPct = CalculatePercentage(spent, cap);
            var expected = CalculateExpected(cap, window);
            var deviation = CalculatePaceDeviation(spent, expected);

            var categoryLimitsTotal = budgets
                .Where(b => NormalizeBucket(ResolveEffectiveBucket(b.CategoryId, b.Category?.ExpenseClass, userBuckets)) == bucket)
                .Sum(b => b.MonthlyLimit);

            buckets.Add(new BucketSummaryResponse
            {
                Bucket = bucket,
                AllocationPct = pct,
                AllocationCap = cap,
                Spent = spent,
                Remaining = cap - spent,
                Percentage = usedPct,
                Status = GetStatus(usedPct),
                ExpectedSpent = expected,
                PaceDeviation = deviation,
                PaceStatus = GetPaceStatus(deviation),
                CategoryLimitsTotal = categoryLimitsTotal,
                OverAllocation = categoryLimitsTotal > cap,
                Categories = rows.Select(x => x.CategoryName).Distinct().ToList()
            });
        }

        return (buckets, uncategorizedRatio);
    }

    private async Task<Guid?> GetUncategorizedIdAsync(CancellationToken cancellationToken)
        => await _dbContext.Categories
            .Where(c => c.CategoryName == UncategorizedName)
            .Select(c => (Guid?)c.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);

    private static decimal CalculatePercentage(decimal spent, decimal limit)
        => limit <= 0 ? 0m : Math.Round(spent / limit * 100m, 2);

    // Pacing: số tiền đáng lẽ đã tiêu = limit × (số ngày đã trôi / tổng ngày trong tháng).
    private static decimal CalculateExpected(decimal limit, MonthWindow window)
    {
        if (limit <= 0 || window.TotalDays <= 0)
            return 0m;

        return Math.Round(limit * window.ElapsedDays / window.TotalDays, 2);
    }

    private static decimal CalculatePaceDeviation(decimal actual, decimal expected)
    {
        if (expected <= 0)
            return actual > 0 ? 1m : 0m;

        return Math.Round((actual - expected) / expected, 4);
    }

    private static string GetPaceStatus(decimal deviation)
    {
        if (deviation <= 0m)
            return deviation < -0.15m ? "UNDER_PACE" : "ON_TRACK";

        return "OVER_PACE";
    }

    private static string GetStatus(decimal usedPercentage)
    {
        if (usedPercentage >= 100m)
            return "RED";

        if (usedPercentage >= WarningThreshold)
            return "YELLOW";

        return "GREEN";
    }

    // Pacing -> điểm: Actual <= Expected = 100đ; vượt 20%→80đ, 50%→50đ, 100%→0đ (tuyến tính).
    private static decimal PacingScore(decimal deviation)
    {
        if (deviation <= 0m)
            return 100m;

        var score = 100m - deviation * 100m;
        return Math.Max(0m, Math.Min(100m, Math.Round(score, 2)));
    }

    private static decimal CalculateBudgetAdherenceScore(List<BucketSummaryResponse> buckets)
    {
        var needs = buckets.FirstOrDefault(b => b.Bucket == "NEEDS");
        var wants = buckets.FirstOrDefault(b => b.Bucket == "WANTS");

        decimal weightSum = 0m;
        decimal weighted = 0m;

        if (needs is not null && needs.AllocationPct > 0)
        {
            weighted += PacingScore(needs.PaceDeviation) * 0.6m;
            weightSum += 0.6m;
        }

        if (wants is not null && wants.AllocationPct > 0)
        {
            weighted += PacingScore(wants.PaceDeviation) * 0.4m;
            weightSum += 0.4m;
        }

        if (weightSum == 0m)
            return 100m;

        return Math.Round(weighted / weightSum, 2);
    }

    private static string NormalizeBucket(string? expenseClass)
    {
        if (string.IsNullOrWhiteSpace(expenseClass))
            return "UNASSIGNED";

        return expenseClass.Trim().ToUpperInvariant() switch
        {
            "NEED" or "NEEDS" => "NEEDS",
            "WANT" or "WANTS" => "WANTS",
            "SAVING" or "SAVINGS" => "SAVINGS",
            _ => "UNASSIGNED"
        };
    }

    private static string? ResolveEffectiveBucket(
        Guid? categoryId, string? defaultBucket, IReadOnlyDictionary<Guid, string> userBuckets)
    {
        if (categoryId.HasValue && userBuckets.TryGetValue(categoryId.Value, out var userBucket))
            return userBucket;

        return defaultBucket;
    }

    private static BudgetAlertPayload CreateBudgetAlert(
        Guid customerId, Budget budget, decimal usedPercentage, decimal spent, decimal crossedThreshold)
    {
        var categoryName = budget.Category?.CategoryName ?? "Budget category";
        var isExceeded = crossedThreshold >= ExceededThreshold;

        var title = isExceeded
            ? $"Budget exceeded: {categoryName}"
            : $"Budget warning: {categoryName}";
        var message = isExceeded
            ? $"{categoryName} has exceeded its limit with {usedPercentage}% used ({spent:0.##}/{budget.MonthlyLimit:0.##})."
            : $"{categoryName} has reached {usedPercentage}% of its budget ({spent:0.##}/{budget.MonthlyLimit:0.##}).";

        return new BudgetAlertPayload(
            title,
            message,
            new Notification
            {
                NotificationId = Guid.NewGuid(),
                CustomerId = customerId,
                CategoryBudgetId = null,
                Title = title,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
    }

    // ── ICT month window ──────────────────────────────────────────────────────
    private static MonthWindow ResolveMonth(string? month)
    {
        int year, mon;
        if (string.IsNullOrWhiteSpace(month))
        {
            var nowIct = DateTime.UtcNow.Add(IctOffset);
            year = nowIct.Year;
            mon = nowIct.Month;
        }
        else
        {
            var parts = month.Split('-');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out year)
                || !int.TryParse(parts[1], out mon)
                || mon < 1 || mon > 12)
            {
                throw new ValidationException("month must be in 'YYYY-MM' format.");
            }
        }

        return BuildMonthWindow(year, mon);
    }

    private static MonthWindow ResolveMonthFromDate(DateOnly date)
        => BuildMonthWindow(date.Year, date.Month);

    private static MonthWindow BuildMonthWindow(int year, int mon)
    {
        var startIct = new DateTimeOffset(year, mon, 1, 0, 0, 0, IctOffset);
        var endIct = startIct.AddMonths(1);
        var totalDays = DateTime.DaysInMonth(year, mon);

        var todayIct = DateOnly.FromDateTime(DateTime.UtcNow.Add(IctOffset));
        var firstDay = new DateOnly(year, mon, 1);

        int elapsedDays;
        if (todayIct < firstDay)
            elapsedDays = 0;
        else if (todayIct >= firstDay.AddMonths(1))
            elapsedDays = totalDays;
        else
            elapsedDays = todayIct.Day;

        return new MonthWindow(
            year, mon, $"{year:D4}-{mon:D2}",
            startIct.UtcDateTime, endIct.UtcDateTime, totalDays, elapsedDays);
    }

    private readonly record struct MonthWindow(
        int Year, int Month, string Key, DateTime StartUtc, DateTime EndExclusiveUtc, int TotalDays, int ElapsedDays);

    private sealed class ScopedSpent
    {
        public Guid CategoryId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Total { get; set; }
    }

    private sealed record BudgetAlertPayload(string Title, string Message, Notification Notification);
}
