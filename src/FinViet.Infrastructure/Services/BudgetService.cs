using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

public class BudgetService : IBudgetService
{
    private const decimal DefaultThresholdPct = 80m;
    private const string SavingsGoalCategoryId = "cat_savings_goal";

    // Tên category catch-all "Chưa phân loại" — không tính vào Budget Adherence.
    private const string UncategorizedName = "Chưa phân loại";

    // Mốc cảnh báo mặc định (business logic 2b: push khi vượt 80% và 100%) — dùng khi customer
    // chưa có customer_settings.notif_budget_thresholds (chưa từng đổi setting nào).
    private static readonly int[] DefaultAlertThresholds = { 80, 100 };

    private readonly FinVietDbContext _dbContext;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;
    private readonly IIncomeAllocationService _incomeAllocationService;
    private readonly ILogger<BudgetService> _logger;

    public BudgetService(
        FinVietDbContext dbContext,
        IBudgetAlertNotifier budgetAlertNotifier,
        IIncomeAllocationService incomeAllocationService,
        ILogger<BudgetService> logger)
    {
        _dbContext = dbContext;
        _budgetAlertNotifier = budgetAlertNotifier;
        _incomeAllocationService = incomeAllocationService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BudgetResponse>> GetBudgetsAsync(
        Guid customerId,
        string? month,
        CancellationToken cancellationToken = default)
    {
        var window = ResolveMonthWindow(month);

        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.CustomerId == customerId)
            .OrderBy(b => b.Category!.SortOrder)
            .ThenBy(b => b.Category!.CategoryName)
            .ToListAsync(cancellationToken);

        var categoryIds = budgets.Select(b => b.CategoryId).Distinct().ToList();
        var spentByScope = await ComputeFlatScopedSpentAsync(customerId, window, categoryIds, cancellationToken);

        return budgets
            .Select(b => BuildFlatBudgetResponse(b, spentByScope))
            .ToList();
    }

    public async Task<BucketSummaryListResponse> GetBudgetBucketsAsync(
        Guid customerId,
        string? month,
        CancellationToken cancellationToken = default)
    {
        var window = ResolveMonthWindow(month);
        var customerExists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerId == customerId, cancellationToken);

        if (!customerExists)
            throw new NotFoundException("Customer not found.");

        // Resolved per requested month rather than read live off Customer, so a change scheduled
        // for next month never retroactively moves this month's (or a past month's) numbers.
        var allocation = await _incomeAllocationService.GetEffectiveAsync(customerId, window.Key, cancellationToken);
        var monthlyIncome = allocation.MonthlyIncome;
        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        // Hũ hiện tại của customer (kéo-thả đổi). Dùng CHUNG để phân loại cả hạn mức lẫn chi tiêu
        // theo bucket, tránh lệch khi category đã được chuyển hũ khác default_bucket.
        var customerBuckets = await _dbContext.CustomerCategories
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .ToDictionaryAsync(x => x.CategoryId, x => x.BucketId, cancellationToken);

        var spentByBucket = await ComputeBucketSpentAsync(customerId, window, cancellationToken);
        var totalSpent = spentByBucket.Sum(x => x.Total);
        var uncategorizedSpent = await ComputeUncategorizedSpentAsync(customerId, window, cancellationToken);
        var uncategorizedRatio = totalSpent > 0 ? Math.Round(uncategorizedSpent / totalSpent * 100m, 2) : 0m;

        var bucketConfigs = new[]
        {
            new { Bucket = "needs", Pct = allocation.NeedsPct },
            new { Bucket = "wants", Pct = allocation.WantsPct },
            new { Bucket = "savings", Pct = allocation.SavingsPct }
        };

        var summaries = new List<BucketSummaryResponse>();
        foreach (var config in bucketConfigs)
        {
            var allocationCap = Math.Round(monthlyIncome * config.Pct / 100m, 2);
            var categoryLimitTotal = budgets
                .Where(b => ResolveCustomerBucket(b.CategoryId, b.Category?.DefaultBucket, customerBuckets)
                    .Equals(config.Bucket, StringComparison.OrdinalIgnoreCase))
                .Sum(b => b.MonthlyLimit);
            var spent = spentByBucket
                .Where(s => s.Bucket.Equals(config.Bucket, StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.Total);
            var expected = CalculateExpectedSpent(Math.Max(allocationCap, categoryLimitTotal), window.Start, window.End);
            var deviation = CalculatePaceDeviation(spent, expected);

            summaries.Add(new BucketSummaryResponse
            {
                Bucket = config.Bucket,
                AllocationPct = config.Pct,
                AllocationCap = allocationCap,
                CategoryLimitTotal = categoryLimitTotal,
                Spent = spent,
                Remaining = allocationCap - spent,
                Percentage = CalculatePercentage(spent, allocationCap),
                OverAllocated = categoryLimitTotal > allocationCap && allocationCap > 0,
                ExpectedSpent = expected,
                PaceDeviation = deviation,
                PaceStatus = GetPaceStatus(deviation)
            });
        }

        return new BucketSummaryListResponse
        {
            Month = window.Key,
            MonthlyIncome = monthlyIncome,
            BudgetAdherenceScore = CalculateFlatBudgetAdherenceScore(summaries),
            UncategorizedRatio = uncategorizedRatio,
            UncategorizedWarning = uncategorizedRatio > 20m,
            Buckets = summaries
        };
    }

    public async Task<BudgetResponse> UpsertBudgetAsync(
        Guid customerId,
        UpsertBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MonthlyLimit <= 0)
            throw new ValidationException("Monthly limit must be greater than 0.");

        var category = await EnsureBudgetCategoryAsync(customerId, request.CategoryId, cancellationToken);
        await EnsureOwnedActiveWalletAsync(customerId, request.WalletId, cancellationToken);

        var budget = request.WalletId.HasValue
            ? await _dbContext.Budgets.FirstOrDefaultAsync(
                b => b.CustomerId == customerId &&
                     b.CategoryId == request.CategoryId &&
                     b.WalletId == request.WalletId.Value,
                cancellationToken)
            : await _dbContext.Budgets.FirstOrDefaultAsync(
                b => b.CustomerId == customerId &&
                     b.CategoryId == request.CategoryId &&
                     b.WalletId == null,
                cancellationToken);

        if (budget is null)
        {
            budget = new Budget
            {
                BudgetId = Guid.NewGuid(),
                CustomerId = customerId,
                CategoryId = request.CategoryId,
                WalletId = request.WalletId,
                MonthlyLimit = request.MonthlyLimit,
                LastAlertThreshold = 0m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Budgets.Add(budget);
        }
        else
        {
            budget.MonthlyLimit = request.MonthlyLimit;
            budget.LastAlertThreshold = 0m;
            budget.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        budget.Category = category;

        var window = ResolveMonthWindow(null);
        var spent = await ComputeFlatScopedSpentAsync(customerId, window, new[] { budget.CategoryId }, cancellationToken);
        return BuildFlatBudgetResponse(budget, spent);
    }

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

        var window = ResolveMonthWindow(null);
        var spent = await ComputeFlatScopedSpentAsync(customerId, window, new[] { budget.CategoryId }, cancellationToken);
        return BuildFlatBudgetResponse(budget, spent);
    }

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

    public async Task SyncBudgetOnTransactionChangeAsync(
        Guid customerId,
        DateOnly affectedDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SyncFlatBudgetsAsync(customerId, affectedDate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to sync budget after transaction change for customer {CustomerId}.",
                customerId);
        }
    }

    private static MonthWindow ResolveMonthWindow(string? month)
    {
        DateOnly firstDay;

        if (string.IsNullOrWhiteSpace(month))
        {
            var localNow = DateTime.UtcNow.AddHours(7);
            firstDay = new DateOnly(localNow.Year, localNow.Month, 1);
        }
        else
        {
            var parts = month.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var year) ||
                !int.TryParse(parts[1], out var parsedMonth) ||
                year < 1 ||
                parsedMonth is < 1 or > 12)
            {
                throw new ValidationException("Month must use yyyy-MM format.");
            }

            firstDay = new DateOnly(year, parsedMonth, 1);
        }

        var endDay = firstDay.AddMonths(1).AddDays(-1);
        var startUtc = firstDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endExclusiveUtc = firstDay.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return new MonthWindow(
            $"{firstDay.Year:D4}-{firstDay.Month:D2}",
            firstDay,
            endDay,
            startUtc,
            endExclusiveUtc);
    }

    private async Task<List<ScopedSpent>> ComputeFlatScopedSpentAsync(
        Guid customerId,
        MonthWindow window,
        IEnumerable<string> categoryIds,
        CancellationToken cancellationToken)
    {
        var ids = categoryIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return new List<ScopedSpent>();

        return await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            where transaction.CategoryId != null
                  && ids.Contains(transaction.CategoryId)
                  && transaction.TransactionType == "expense"
                  && transaction.TransactionDate >= window.StartUtc
                  && transaction.TransactionDate < window.EndExclusiveUtc
                  && wallet.CustomerId == customerId
            group transaction by new { CategoryId = transaction.CategoryId!, transaction.WalletId } into grouped
            select new ScopedSpent
            {
                CategoryId = grouped.Key.CategoryId,
                WalletId = grouped.Key.WalletId,
                Total = grouped.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<List<BucketSpent>> ComputeBucketSpentAsync(
        Guid customerId,
        MonthWindow window,
        CancellationToken cancellationToken)
    {
        var spentRows = await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            join category in _dbContext.Categories.AsNoTracking()
                on transaction.CategoryId equals category.CategoryId
            where wallet.CustomerId == customerId
                  && transaction.CategoryId != null
                  && transaction.CategoryId != SavingsGoalCategoryId
                  && category.CategoryName != UncategorizedName
                  && transaction.TransactionType == "expense"
                  && transaction.TransactionDate >= window.StartUtc
                  && transaction.TransactionDate < window.EndExclusiveUtc
            select new
            {
                transaction.CategoryId,
                transaction.Amount,
                category.DefaultBucket
            })
            .ToListAsync(cancellationToken);

        if (spentRows.Count == 0)
            return new List<BucketSpent>();

        var categoryIds = spentRows
            .Select(x => x.CategoryId!)
            .Distinct()
            .ToList();

        var customerBuckets = await _dbContext.CustomerCategories
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId &&
                        x.IsActive &&
                        categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(
                x => x.CategoryId,
                x => x.BucketId,
                cancellationToken);

        return spentRows
            .GroupBy(x => ResolveCustomerBucket(x.CategoryId!, x.DefaultBucket, customerBuckets))
            .Select(group => new BucketSpent
            {
                Bucket = group.Key,
                Total = group.Sum(x => x.Amount)
            })
            .ToList();
    }

    private async Task<decimal> ComputeUncategorizedSpentAsync(
        Guid customerId,
        MonthWindow window,
        CancellationToken cancellationToken)
    {
        var uncategorizedId = await _dbContext.Categories
            .Where(c => c.CategoryName == UncategorizedName)
            .Select(c => (string?)c.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);

        return await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            where wallet.CustomerId == customerId
                  && transaction.TransactionType == "expense"
                  && (transaction.CategoryId == null ||
                      (uncategorizedId != null && transaction.CategoryId == uncategorizedId))
                  && transaction.TransactionDate >= window.StartUtc
                  && transaction.TransactionDate < window.EndExclusiveUtc
            select transaction.Amount)
            .SumAsync(cancellationToken);
    }

    private BudgetResponse BuildFlatBudgetResponse(Budget budget, IReadOnlyList<ScopedSpent> spentByScope)
    {
        var spent = budget.WalletId.HasValue
            ? spentByScope
                .Where(s => s.CategoryId == budget.CategoryId && s.WalletId == budget.WalletId.Value)
                .Sum(s => s.Total)
            : spentByScope
                .Where(s => s.CategoryId == budget.CategoryId)
                .Sum(s => s.Total);

        var percentage = CalculatePercentage(spent, budget.MonthlyLimit);

        return new BudgetResponse
        {
            Id = budget.BudgetId,
            CategoryId = budget.CategoryId,
            CategoryName = budget.Category?.CategoryName ?? budget.CategoryId,
            WalletId = budget.WalletId,
            MonthlyLimit = budget.MonthlyLimit,
            Spent = spent,
            Remaining = budget.MonthlyLimit - spent,
            Percentage = percentage,
            Status = GetStatus(percentage, DefaultThresholdPct),
            Bucket = ToBudgetBucketId(budget.Category?.DefaultBucket)
        };
    }

    private async Task<Category> EnsureBudgetCategoryAsync(
        Guid customerId,
        string categoryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            throw new ValidationException("Category id is required.");

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(c => c.CategoryId == categoryId, cancellationToken);

        if (category is null)
            throw new NotFoundException("Category", categoryId);

        if (!category.Type.Equals("expense", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Budgets can only be created for expense categories.");

        if (category.CategoryId == SavingsGoalCategoryId ||
            category.CategoryName.Equals(UncategorizedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("This category cannot be budgeted directly.");
        }

        var hasCategorySet = await _dbContext.CustomerCategories
            .AnyAsync(x => x.CustomerId == customerId, cancellationToken);

        if (!hasCategorySet)
            await SeedCustomerCategoriesAsync(customerId, cancellationToken);

        var isAvailable = await _dbContext.CustomerCategories
            .AnyAsync(
                x => x.CustomerId == customerId &&
                     x.CategoryId == categoryId &&
                     x.IsActive,
                cancellationToken);

        if (!isAvailable)
            throw new ValidationException("Category is not available for this customer.");

        return category;
    }

    private async Task SeedCustomerCategoriesAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var existingCustomer = await _dbContext.Customers
            .AnyAsync(c => c.CustomerId == customerId, cancellationToken);

        if (!existingCustomer)
            throw new NotFoundException("Customer", customerId);

        var categories = await _dbContext.Categories
            .AsNoTracking()
            .Where(c =>
                c.Type == "expense" &&
                c.CategoryId != SavingsGoalCategoryId &&
                c.CategoryName != UncategorizedName)
            .Select(c => new
            {
                c.CategoryId,
                c.DefaultBucket
            })
            .ToListAsync(cancellationToken);

        foreach (var category in categories)
        {
            _dbContext.CustomerCategories.Add(new CustomerCategory
            {
                CustomerId = customerId,
                CategoryId = category.CategoryId,
                BucketId = ToBudgetBucketId(category.DefaultBucket),
                Source = "system",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureOwnedActiveWalletAsync(
        Guid customerId,
        Guid? walletId,
        CancellationToken cancellationToken)
    {
        if (!walletId.HasValue)
            return;

        var exists = await _dbContext.Wallets
            .AnyAsync(
                x => x.CustomerId == customerId &&
                     x.WalletId == walletId.Value &&
                     !x.IsDeleted,
                cancellationToken);

        if (!exists)
            throw new NotFoundException("Wallet", walletId.Value);
    }

    private static decimal CalculateFlatBudgetAdherenceScore(IReadOnlyList<BucketSummaryResponse> buckets)
    {
        var scoredBuckets = buckets
            .Where(x => x.Bucket is "needs" or "wants")
            .ToList();

        var weightSum = scoredBuckets.Sum(x => x.AllocationPct);
        if (weightSum == 0m)
            return 100m;

        var weighted = scoredBuckets.Sum(x => PacingScore(x.PaceDeviation) * x.AllocationPct);
        return Math.Round(weighted / weightSum, 2);
    }

    private async Task SyncFlatBudgetsAsync(
        Guid customerId,
        DateOnly affectedDate,
        CancellationToken cancellationToken)
    {
        var window = ResolveMonthWindow($"{affectedDate.Year:D4}-{affectedDate.Month:D2}");
        var budgets = await _dbContext.Budgets
            .Include(x => x.Category)
            .Where(x => x.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
            return;

        var customerThresholds = await _dbContext.CustomerSettings
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .Select(s => s.NotifBudgetThresholds)
            .FirstOrDefaultAsync(cancellationToken);
        var thresholds = customerThresholds is { Length: 2 } ? customerThresholds : DefaultAlertThresholds;
        var warningThreshold = (decimal)thresholds[0];
        var exceededThreshold = (decimal)thresholds[1];

        var spentByScope = await ComputeFlatScopedSpentAsync(
            customerId,
            window,
            budgets.Select(x => x.CategoryId),
            cancellationToken);

        var pendingAlerts = new List<FlatBudgetAlertPayload>();

        foreach (var budget in budgets)
        {
            if (budget.LastAlertMonth != window.Key)
            {
                budget.LastAlertMonth = window.Key;
                budget.LastAlertThreshold = 0m;
            }

            var response = BuildFlatBudgetResponse(budget, spentByScope);
            var crossedThreshold = response.Percentage >= exceededThreshold
                ? exceededThreshold
                : response.Percentage >= warningThreshold
                    ? warningThreshold
                    : 0m;

            if (crossedThreshold > budget.LastAlertThreshold)
            {
                pendingAlerts.Add(CreateFlatBudgetAlert(
                    customerId,
                    response,
                    crossedThreshold,
                    exceededThreshold));
                budget.LastAlertThreshold = crossedThreshold;
            }
            else if (response.Percentage < warningThreshold && budget.LastAlertThreshold > 0m)
            {
                budget.LastAlertThreshold = 0m;
            }

            budget.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var alert in pendingAlerts)
        {
            await _budgetAlertNotifier.SendBudgetAlertAsync(
                customerId,
                alert.BudgetId,
                alert.Title,
                alert.Message,
                cancellationToken);
        }
    }

    private static FlatBudgetAlertPayload CreateFlatBudgetAlert(
        Guid customerId,
        BudgetResponse budget,
        decimal crossedThreshold,
        decimal exceededThreshold)
    {
        var isExceeded = crossedThreshold >= exceededThreshold;
        var title = isExceeded
            ? $"Budget exceeded: {budget.CategoryName}"
            : $"Budget warning: {budget.CategoryName}";
        var message = isExceeded
            ? $"{budget.CategoryName} has exceeded its limit with {budget.Percentage}% used ({budget.Spent:0.##}/{budget.MonthlyLimit:0.##})."
            : $"{budget.CategoryName} has reached {budget.Percentage}% of its budget ({budget.Spent:0.##}/{budget.MonthlyLimit:0.##}).";

        return new FlatBudgetAlertPayload(customerId, budget.Id, title, message);
    }

    private static string ToBudgetBucketId(string? expenseClass)
        => NormalizeBucket(expenseClass) switch
        {
            "WANTS" => "wants",
            "SAVINGS" => "savings",
            _ => "needs"
        };

    // Hũ áp dụng cho 1 category: ưu tiên hũ customer đã chọn (kéo-thả), fallback default_bucket.
    private static string ResolveCustomerBucket(
        string categoryId,
        string? expenseClass,
        IReadOnlyDictionary<string, string> customerBuckets)
        => customerBuckets.TryGetValue(categoryId, out var bucket)
            ? bucket
            : ToBudgetBucketId(expenseClass);

    private static decimal CalculatePercentage(decimal spent, decimal limit)
    {
        if (limit <= 0)
            return 0m;

        return Math.Round(spent / limit * 100m, 2);
    }

    // Pacing: số tiền đáng lẽ đã tiêu = budget × (số ngày đã trôi / tổng số ngày).
    private static decimal CalculateExpectedSpent(decimal limit, DateOnly start, DateOnly end)
    {
        if (limit <= 0)
            return 0m;

        var totalDays = end.DayNumber - start.DayNumber + 1;
        if (totalDays <= 0)
            return 0m;

        // "Hôm nay" theo ICT (UTC+7) để khớp biên kỳ do ResolveMonthWindow tính cùng múi giờ.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

        // Trước kỳ → chưa nên tiêu gì; sau kỳ → cả kỳ.
        int elapsedDays;
        if (today < start)
            elapsedDays = 0;
        else if (today > end)
            elapsedDays = totalDays;
        else
            elapsedDays = today.DayNumber - start.DayNumber + 1;

        var expected = limit * elapsedDays / totalDays;
        return Math.Round(expected, 2);
    }

    private static decimal CalculatePaceDeviation(decimal actual, decimal expected)
    {
        if (expected <= 0)
            return actual > 0 ? 1m : 0m;

        return Math.Round((actual - expected) / expected, 4);
    }

    // ON_TRACK nếu actual ≤ expected (deviation ≤ 0).
    private static string GetPaceStatus(decimal deviation)
    {
        if (deviation <= 0m)
            return deviation < -0.15m ? "UNDER_PACE" : "ON_TRACK";

        return "OVER_PACE";
    }

    private static string GetStatus(decimal usedPercentage, decimal warningThreshold)
    {
        if (usedPercentage >= 100m)
            return "RED";

        if (usedPercentage >= warningThreshold)
            return "YELLOW";

        return "GREEN";
    }

    // Pacing -> điểm: Actual <= Expected (deviation <= 0) = 100đ.
    private static decimal PacingScore(decimal deviation)
    {
        if (deviation <= 0m)
            return 100m;

        var score = 100m - deviation * 100m;
        return Math.Max(0m, Math.Min(100m, Math.Round(score, 2)));
    }

    // Chuẩn hóa bucket về NEEDS/WANTS/SAVINGS dựa trên ExpenseClass.
    private static string NormalizeBucket(string? expenseClass)
    {
        if (string.IsNullOrWhiteSpace(expenseClass))
            return "UNASSIGNED";

        var value = expenseClass.Trim().ToUpperInvariant();
        return value switch
        {
            "NEED" or "NEEDS" => "NEEDS",
            "WANT" or "WANTS" => "WANTS",
            "SAVING" or "SAVINGS" => "SAVINGS",
            _ => "UNASSIGNED"
        };
    }

    private sealed class ScopedSpent
    {
        public string CategoryId { get; set; } = string.Empty;
        public Guid WalletId { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class BucketSpent
    {
        public string Bucket { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }

    private sealed record MonthWindow(
        string Key,
        DateOnly Start,
        DateOnly End,
        DateTime StartUtc,
        DateTime EndExclusiveUtc);

    private sealed record FlatBudgetAlertPayload(
        Guid CustomerId,
        Guid BudgetId,
        string Title,
        string Message);
}
