using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

public class BudgetService : IBudgetService
{
    private const decimal DefaultThresholdPct = 80m;

    // Tên category catch-all "Chưa phân loại" — không tính vào Budget Adherence.
    private const string UncategorizedName = "Chưa phân loại";

    // Các mốc cảnh báo (business logic 2b: push khi vượt 80% và 100%).
    private const decimal WarningThreshold = 80m;
    private const decimal ExceededThreshold = 100m;

    private readonly FinVietDbContext _dbContext;
    private readonly IBudgetAlertNotifier _budgetAlertNotifier;

    public BudgetService(
        FinVietDbContext dbContext,
        IBudgetAlertNotifier budgetAlertNotifier)
    {
        _dbContext = dbContext;
        _budgetAlertNotifier = budgetAlertNotifier;
    }

    public async Task<IReadOnlyList<BudgetPlanResponse>> GetBudgetPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var plans = await _dbContext.BudgetPlans
            .AsNoTracking()
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        return plans
            .Select(plan => BuildBudgetPlanResponse(plan))
            .ToList();
    }

    public async Task<BudgetPlanResponse> CreateBudgetPlanAsync(
        Guid customerId,
        CreateBudgetPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanName))
            throw new ValidationException("Plan name is required.");

        if (request.StartDate > request.EndDate)
            throw new ValidationException("Start date must be before end date.");

        if (request.CategoryBudgets.Count == 0)
            throw new ValidationException("At least one category budget is required.");

        var customerExists = await _dbContext.Customers
            .AnyAsync(x => x.CustomerId == customerId, cancellationToken);

        if (!customerExists)
            throw new NotFoundException("Customer not found.");

        // Mỗi (category, wallet) chỉ được khai báo một lần trong cùng request.
        var scopeKeys = request.CategoryBudgets
            .Select(x => (x.CategoryId, x.WalletId))
            .ToList();

        if (scopeKeys.Count != scopeKeys.Distinct().Count())
            throw new ValidationException("Duplicate category/wallet budgets are not allowed.");
        if (request.CategoryBudgets.Count == 0)
            throw new ValidationException("At least one category budget is required.");

        // Phân bổ 50-30-20: nếu không truyền thì mặc định 50/30/20.
        var needsPct = request.NeedsPct ?? 50m;
        var wantsPct = request.WantsPct ?? 30m;
        var savingsPct = request.SavingsPct ?? 20m;

        if (needsPct < 0 || wantsPct < 0 || savingsPct < 0)
            throw new ValidationException("Bucket percentages cannot be negative.");

        if (Math.Abs(needsPct + wantsPct + savingsPct - 100m) > 0.01m)
            throw new ValidationException("Bucket percentages (Needs + Wants + Savings) must sum to 100.");
        var distinctCategoryIds = request.CategoryBudgets
            .Select(x => x.CategoryId)
            .Distinct()
            .ToList();

        var categories = await _dbContext.Categories
            .Where(x => distinctCategoryIds.Contains(x.CategoryId))
            .ToListAsync(cancellationToken);

        if (categories.Count != distinctCategoryIds.Count)
            throw new NotFoundException("Some categories do not exist.");

        // Validate các ví được tham chiếu phải thuộc về customer.
        var walletIds = request.CategoryBudgets
            .Where(x => x.WalletId.HasValue)
            .Select(x => x.WalletId!.Value)
            .Distinct()
            .ToList();

        if (walletIds.Count > 0)
        {
            var ownedWalletCount = await _dbContext.Wallets
                .CountAsync(w => w.CustomerId == customerId && walletIds.Contains(w.WalletId), cancellationToken);

            if (ownedWalletCount != walletIds.Count)
                throw new ValidationException("One or more wallets do not belong to this customer.");
        }

        foreach (var item in request.CategoryBudgets)
        {
            if (item.AmountLimit <= 0)
                throw new ValidationException("Amount limit must be greater than 0.");

            if (item.ThresholdPct is < 0 or > 100)
                throw new ValidationException("Threshold percentage must be between 0 and 100.");
        }

        var budgetPlan = new BudgetPlan
        {
            PlanId = Guid.NewGuid(),
            CustomerId = customerId,
            PlanName = request.PlanName.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NeedsPct = needsPct,
            WantsPct = wantsPct,
            SavingsPct = savingsPct,
            CategoryBudgets = request.CategoryBudgets.Select(x => new CategoryBudget
            {
                CategoryBudgetId = Guid.NewGuid(),
                CategoryId = x.CategoryId,
                WalletId = x.WalletId,
                AmountLimit = x.AmountLimit,
                CurrentSpent = 0m,
                ThresholdPct = x.ThresholdPct ?? DefaultThresholdPct,
                ThresholdType = string.IsNullOrWhiteSpace(x.ThresholdType)
                    ? "PERCENT"
                    : x.ThresholdType.Trim(),
                LastAlertThreshold = 0m
            }).ToList()
        };

        _dbContext.BudgetPlans.Add(budgetPlan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetBudgetPlanResponseAsync(customerId, budgetPlan.PlanId, cancellationToken);
    }

    public async Task<BudgetTrackingResponse> GetBudgetTrackingAsync(
        Guid customerId,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(
                x => x.PlanId == planId && x.CustomerId == customerId,
                cancellationToken);

        if (plan is null)
            throw new NotFoundException("Budget plan not found.");

        await SyncCurrentSpentAsync(plan, customerId, cancellationToken);

        return BuildTrackingResponse(plan);
    }

    public async Task<BudgetTrackingResponse> GetCurrentBudgetTrackingAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var plan = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .Where(x =>
                x.CustomerId == customerId &&
                x.StartDate <= today &&
                x.EndDate >= today)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            throw new NotFoundException("No active budget plan found for current month.");

        await SyncCurrentSpentAsync(plan, customerId, cancellationToken);

        return BuildTrackingResponse(plan);
    }

    public async Task<CategoryBudgetResponse> UpdateCategoryBudgetAsync(
        Guid customerId,
        Guid categoryBudgetId,
        UpdateCategoryBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        var categoryBudget = await _dbContext.CategoryBudgets
            .Include(x => x.Plan)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(
                x => x.CategoryBudgetId == categoryBudgetId &&
                     x.Plan != null &&
                     x.Plan.CustomerId == customerId,
                cancellationToken);

        if (categoryBudget is null)
            throw new NotFoundException("Category budget not found.");

        if (request.AmountLimit.HasValue)
        {
            if (request.AmountLimit.Value <= 0)
                throw new ValidationException("Amount limit must be greater than 0.");

            categoryBudget.AmountLimit = request.AmountLimit.Value;
            // Đổi hạn mức → reset mốc alert để đánh giá lại từ đầu.
            categoryBudget.LastAlertThreshold = 0m;
        }

        if (request.ThresholdPct.HasValue)
        {
            if (request.ThresholdPct.Value < 0 || request.ThresholdPct.Value > 100)
                throw new ValidationException("Threshold percentage must be between 0 and 100.");

            categoryBudget.ThresholdPct = request.ThresholdPct.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.ThresholdType))
            categoryBudget.ThresholdType = request.ThresholdType.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var plan = categoryBudget.Plan!;
        return BuildCategoryBudgetResponse(categoryBudget, plan.StartDate, plan.EndDate);
    }

    public async Task<bool> DeleteBudgetPlanAsync(
        Guid customerId,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.BudgetPlans
            .FirstOrDefaultAsync(
                x => x.PlanId == planId && x.CustomerId == customerId,
                cancellationToken);

        if (plan is null)
            throw new NotFoundException("Budget plan not found.");

        _dbContext.BudgetPlans.Remove(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<BudgetHistoryResponse>> GetBudgetHistoryAsync(
        Guid customerId,
        int year,
        CancellationToken cancellationToken = default)
    {
        var fromDate = new DateOnly(year, 1, 1);
        var toDate = new DateOnly(year, 12, 31);

        var plans = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .Where(x =>
                x.CustomerId == customerId &&
                x.StartDate >= fromDate &&
                x.StartDate <= toDate)
            .OrderBy(x => x.StartDate)
            .ToListAsync(cancellationToken);

        var result = new List<BudgetHistoryResponse>();
        BudgetHistoryResponse? previousHistory = null;

        foreach (var plan in plans)
        {
            await SyncCurrentSpentAsync(plan, customerId, cancellationToken);

            var tracking = BuildTrackingResponse(plan);
            var historyItem = new BudgetHistoryResponse
            {
                PlanId = plan.PlanId,
                PlanName = plan.PlanName,
                Month = plan.StartDate.Month,
                Year = plan.StartDate.Year,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                TotalLimit = tracking.TotalLimit,
                TotalSpent = tracking.TotalSpent,
                UsedPercentage = tracking.UsedPercentage,
                PreviousMonthTotalSpent = previousHistory?.TotalSpent,
                TotalSpentChange = previousHistory is null
                    ? null
                    : tracking.TotalSpent - previousHistory.TotalSpent,
                PreviousMonthUsedPercentage = previousHistory?.UsedPercentage,
                UsedPercentageChange = previousHistory is null
                    ? null
                    : tracking.UsedPercentage - previousHistory.UsedPercentage,
                Status = tracking.Status
            };

            result.Add(historyItem);
            previousHistory = historyItem;
        }

        return result;
    }

    public async Task<BudgetPlanResponse> ResetCurrentMonthBudgetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);

        var currentPlan = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(
                x => x.CustomerId == customerId &&
                     x.StartDate == currentMonthStart &&
                     x.EndDate == currentMonthEnd,
                cancellationToken);

        if (currentPlan is not null)
        {
            await SyncCurrentSpentAsync(currentPlan, customerId, cancellationToken);
            return BuildBudgetPlanResponse(currentPlan);
        }

        var lastPlan = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
            .Where(x =>
                x.CustomerId == customerId &&
                x.EndDate < currentMonthStart)
            .OrderByDescending(x => x.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastPlan is null)
            throw new NotFoundException("No previous budget plan found to reset from.");

        var newPlan = new BudgetPlan
        {
            PlanId = Guid.NewGuid(),
            CustomerId = customerId,
            PlanName = $"Budget {today.Month}/{today.Year}",
            StartDate = currentMonthStart,
            EndDate = currentMonthEnd,
            NeedsPct = lastPlan.NeedsPct,
            WantsPct = lastPlan.WantsPct,
            SavingsPct = lastPlan.SavingsPct,
            CategoryBudgets = lastPlan.CategoryBudgets.Select(x => new CategoryBudget
            {
                CategoryBudgetId = Guid.NewGuid(),
                CategoryId = x.CategoryId,
                WalletId = x.WalletId,
                AmountLimit = x.AmountLimit,
                CurrentSpent = 0m,
                ThresholdPct = x.ThresholdPct ?? DefaultThresholdPct,
                ThresholdType = x.ThresholdType,
                LastAlertThreshold = 0m
            }).ToList()
        };

        _dbContext.BudgetPlans.Add(newPlan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetBudgetPlanResponseAsync(customerId, newPlan.PlanId, cancellationToken);
    }

    private async Task<BudgetPlanResponse> GetBudgetPlanResponseAsync(
        Guid customerId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var plan = await _dbContext.BudgetPlans
            .Include(x => x.CategoryBudgets)
                .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(
                x => x.PlanId == planId && x.CustomerId == customerId,
                cancellationToken);

        if (plan is null)
            throw new NotFoundException("Budget plan not found.");

        return BuildBudgetPlanResponse(plan);
    }

    // Tính lại current_spent cho mỗi category budget, có hỗ trợ scope per-wallet,
    // loại trừ giao dịch "Chưa phân loại", và phát alert đúng tại mốc 80% / 100%.
    private async Task SyncCurrentSpentAsync(
        BudgetPlan plan,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var startUtc = plan.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endExclusiveUtc = plan.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Id của category catch-all để loại khỏi Budget Adherence.
        var uncategorizedId = await _dbContext.Categories
            .Where(c => c.CategoryName == UncategorizedName)
            .Select(c => (Guid?)c.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);

        var categoryIds = plan.CategoryBudgets
            .Where(x => x.CategoryId.HasValue)
            .Select(x => x.CategoryId!.Value)
            .Distinct()
            .ToList();

        // Tổng chi theo (category, wallet) — group ở mức wallet để map cả 2 scope.
        var spentByScope = categoryIds.Count == 0
            ? new List<ScopedSpent>()
            : await (
                from transaction in _dbContext.Transactions.AsNoTracking()
                join wallet in _dbContext.Wallets.AsNoTracking()
                    on transaction.WalletId equals wallet.WalletId
                where transaction.CategoryId.HasValue
                      && categoryIds.Contains(transaction.CategoryId.Value)
                      && (uncategorizedId == null || transaction.CategoryId.Value != uncategorizedId.Value)
                      && transaction.TransactionType == "EXPENSE"
                      && transaction.TransactionDate >= startUtc
                      && transaction.TransactionDate < endExclusiveUtc
                      && wallet.CustomerId == customerId
                group transaction by new { CategoryId = transaction.CategoryId!.Value, transaction.WalletId } into grouped
                select new ScopedSpent
                {
                    CategoryId = grouped.Key.CategoryId,
                    WalletId = grouped.Key.WalletId,
                    Total = grouped.Sum(x => x.Amount)
                })
                .ToListAsync(cancellationToken);

        var pendingAlerts = new List<BudgetAlertPayload>();

        foreach (var categoryBudget in plan.CategoryBudgets)
        {
            if (!categoryBudget.CategoryId.HasValue)
            {
                categoryBudget.CurrentSpent = 0m;
                continue;
            }

            var cbCategoryId = categoryBudget.CategoryId.Value;

            // per-wallet → chỉ lấy đúng ví; per-category (WalletId null) → cộng mọi ví.
            var spent = categoryBudget.WalletId.HasValue
                ? spentByScope
                    .Where(s => s.CategoryId == cbCategoryId && s.WalletId == categoryBudget.WalletId.Value)
                    .Sum(s => s.Total)
                : spentByScope
                    .Where(s => s.CategoryId == cbCategoryId)
                    .Sum(s => s.Total);

            categoryBudget.CurrentSpent = spent;

            var usedPercentage = CalculatePercentage(spent, categoryBudget.AmountLimit);

            // Xác định mốc đã vượt: 100 ưu tiên trước, rồi 80.
            var crossedThreshold = usedPercentage >= ExceededThreshold
                ? ExceededThreshold
                : usedPercentage >= WarningThreshold
                    ? WarningThreshold
                    : 0m;

            // Chỉ alert khi vượt một mốc CAO HƠN mốc đã alert lần trước.
            if (crossedThreshold > categoryBudget.LastAlertThreshold)
            {
                var alert = CreateBudgetAlert(customerId, categoryBudget, usedPercentage, spent, crossedThreshold);
                if (alert is not null)
                {
                    pendingAlerts.Add(alert);
                    _dbContext.Notifications.Add(alert.Notification);
                }

                categoryBudget.LastAlertThreshold = crossedThreshold;
            }
            else if (usedPercentage < WarningThreshold && categoryBudget.LastAlertThreshold > 0m)
            {
                // Tụt xuống dưới 80% (vd: tăng hạn mức) → reset để được alert lại sau này.
                categoryBudget.LastAlertThreshold = 0m;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var alert in pendingAlerts)
        {
            await _budgetAlertNotifier.SendBudgetAlertAsync(
                customerId,
                alert.Title,
                alert.Message,
                cancellationToken);
        }
    }

    private static BudgetPlanResponse BuildBudgetPlanResponse(BudgetPlan plan)
    {
        return new BudgetPlanResponse
        {
            PlanId = plan.PlanId,
            CustomerId = plan.CustomerId,
            PlanName = plan.PlanName,
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            CategoryBudgets = plan.CategoryBudgets
                .Select(cb => BuildCategoryBudgetResponse(cb, plan.StartDate, plan.EndDate))
                .ToList()
        };
    }

    private static BudgetTrackingResponse BuildTrackingResponse(BudgetPlan plan)
    {
        var categories = plan.CategoryBudgets
            .Select(cb => BuildCategoryBudgetResponse(cb, plan.StartDate, plan.EndDate))
            .ToList();

        var totalLimit = categories.Sum(x => x.AmountLimit);
        var totalSpent = categories.Sum(x => x.CurrentSpent);
        var usedPercentage = CalculatePercentage(totalSpent, totalLimit);

        var expectedSpent = CalculateExpectedSpent(totalLimit, plan.StartDate, plan.EndDate);
        var paceDeviation = CalculatePaceDeviation(totalSpent, expectedSpent);

        return new BudgetTrackingResponse
        {
            PlanId = plan.PlanId,
            PlanName = plan.PlanName,
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            TotalLimit = totalLimit,
            TotalSpent = totalSpent,
            TotalRemaining = totalLimit - totalSpent,
            UsedPercentage = usedPercentage,
            Status = GetStatus(usedPercentage, DefaultThresholdPct),
            ExpectedSpent = expectedSpent,
            PaceDeviation = paceDeviation,
            PaceStatus = GetPaceStatus(paceDeviation),
            Categories = categories
        };
    }

    private static CategoryBudgetResponse BuildCategoryBudgetResponse(
        CategoryBudget categoryBudget,
        DateOnly planStart,
        DateOnly planEnd)
    {
        var currentSpent = categoryBudget.CurrentSpent ?? 0m;
        var threshold = categoryBudget.ThresholdPct ?? DefaultThresholdPct;
        var usedPercentage = CalculatePercentage(currentSpent, categoryBudget.AmountLimit);

        var expectedSpent = CalculateExpectedSpent(categoryBudget.AmountLimit, planStart, planEnd);
        var paceDeviation = CalculatePaceDeviation(currentSpent, expectedSpent);

        return new CategoryBudgetResponse
        {
            CategoryBudgetId = categoryBudget.CategoryBudgetId,
            CategoryId = categoryBudget.CategoryId,
            CategoryName = categoryBudget.Category?.CategoryName ?? string.Empty,
            WalletId = categoryBudget.WalletId,
            AmountLimit = categoryBudget.AmountLimit,
            CurrentSpent = currentSpent,
            RemainingAmount = categoryBudget.AmountLimit - currentSpent,
            UsedPercentage = usedPercentage,
            ThresholdPct = categoryBudget.ThresholdPct,
            Status = GetStatus(usedPercentage, threshold),
            ExpectedSpent = expectedSpent,
            PaceDeviation = paceDeviation,
            PaceStatus = GetPaceStatus(paceDeviation)
        };
    }

    private static decimal CalculatePercentage(decimal spent, decimal limit)
    {
        if (limit <= 0)
            return 0m;

        return Math.Round(spent / limit * 100m, 2);
    }

    // Pacing: số tiền đáng lẽ đã tiêu = budget × (số ngày đã trôi / tổng số ngày).
    // Business logic mục 3 Metric 2 và mục 6.
    private static decimal CalculateExpectedSpent(decimal limit, DateOnly start, DateOnly end)
    {
        if (limit <= 0)
            return 0m;

        var totalDays = end.DayNumber - start.DayNumber + 1;
        if (totalDays <= 0)
            return 0m;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

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
    // OVER_PACE nếu vượt pacing; UNDER_PACE giữ riêng khi tiêu chậm hơn nhiều.
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

    private static BudgetAlertPayload? CreateBudgetAlert(
        Guid customerId,
        CategoryBudget categoryBudget,
        decimal usedPercentage,
        decimal spent,
        decimal crossedThreshold)
    {
        if (!categoryBudget.CategoryId.HasValue)
            return null;

        var categoryName = categoryBudget.Category?.CategoryName ?? "Budget category";
        var isExceeded = crossedThreshold >= ExceededThreshold;

        var title = isExceeded
            ? $"Budget exceeded: {categoryName}"
            : $"Budget warning: {categoryName}";
        var message = isExceeded
            ? $"{categoryName} has exceeded its limit with {usedPercentage}% used ({spent:0.##}/{categoryBudget.AmountLimit:0.##})."
            : $"{categoryName} has reached {usedPercentage}% of its budget ({spent:0.##}/{categoryBudget.AmountLimit:0.##}).";

        return new BudgetAlertPayload(
            title,
            message,
            new Notification
            {
                NotificationId = Guid.NewGuid(),
                CustomerId = customerId,
                CategoryBudgetId = categoryBudget.CategoryBudgetId,
                Title = title,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
    }

    public async Task<BucketTrackingResponse> GetBucketTrackingAsync(Guid customerId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.BudgetPlans
        .AsNoTracking()
        .FirstOrDefaultAsync(
            x => x.PlanId == planId && x.CustomerId == customerId,
            cancellationToken);

        if (plan is null)
            throw new NotFoundException("Budget plan not found.");

        return await BuildBucketTrackingAsync(plan, customerId, cancellationToken);
    }

    public async Task<BucketTrackingResponse> GetCurrentBucketTrackingAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var plan = await _dbContext.BudgetPlans
            .AsNoTracking()
            .Where(x =>
                x.CustomerId == customerId &&
                x.StartDate <= today &&
                x.EndDate >= today)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            throw new NotFoundException("No active budget plan found for current month.");

        return await BuildBucketTrackingAsync(plan, customerId, cancellationToken);
    }
    // Tính tracking theo mô hình 50-30-20: limit từng bucket = income x %,
    // spent gom theo bucket hiệu lực của category, loại "Chưa phân loại",
    // pacing và Budget Adherence weighted (Needs > Wants).
    private async Task<BucketTrackingResponse> BuildBucketTrackingAsync(BudgetPlan plan, Guid customerId, CancellationToken cancellationToken)
    {
        var income = await _dbContext.Customers
        .Where(c => c.CustomerId == customerId)
        .Select(c => c.MonthlyIncomeExpected)
        .FirstOrDefaultAsync(cancellationToken);

        if (income is null or <= 0)
            throw new ValidationException(
                "Monthly income is required to use the 50-30-20 model. Please set it in onboarding.");

        var monthlyIncome = income.Value;

        var startUtc = plan.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endExclusiveUtc = plan.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var uncategorizedId = await _dbContext.Categories
            .Where(c => c.CategoryName == UncategorizedName)
            .Select(c => (Guid?)c.CategoryId)
            .FirstOrDefaultAsync(cancellationToken);

        // Gom chi tiêu theo bucket trong kỳ, chỉ EXPENSE của customer.
        // Bucket được xác định bằng ExpenseClass của category.
        var spentRows = await (
            from transaction in _dbContext.Transactions.AsNoTracking()
            join wallet in _dbContext.Wallets.AsNoTracking()
                on transaction.WalletId equals wallet.WalletId
            join category in _dbContext.Categories.AsNoTracking()
                on transaction.CategoryId equals category.CategoryId
            where wallet.CustomerId == customerId
                  && transaction.CategoryId != null
                  && transaction.TransactionType == "EXPENSE"
                  && transaction.TransactionDate >= startUtc
                  && transaction.TransactionDate < endExclusiveUtc
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

        var allocations = new (string Bucket, decimal Pct)[]
        {
        ("NEEDS", plan.NeedsPct),
        ("WANTS", plan.WantsPct),
        ("SAVINGS", plan.SavingsPct)
        };

        var buckets = new List<BucketBudgetResponse>();

        foreach (var (bucket, pct) in allocations)
        {
            var limit = Math.Round(monthlyIncome * pct / 100m, 2);

            var rows = spentRows
                .Where(x => NormalizeBucket(x.ExpenseClass) == bucket
                            && (uncategorizedId is null || x.CategoryId != uncategorizedId.Value))
                .ToList();

            var spent = rows.Sum(x => x.Amount);
            var usedPct = CalculatePercentage(spent, limit);
            var expected = CalculateExpectedSpent(limit, plan.StartDate, plan.EndDate);
            var deviation = CalculatePaceDeviation(spent, expected);

            buckets.Add(new BucketBudgetResponse
            {
                Bucket = bucket,
                AllocationPct = pct,
                LimitAmount = limit,
                SpentAmount = spent,
                RemainingAmount = limit - spent,
                UsedPercentage = usedPct,
                Status = GetStatus(usedPct, DefaultThresholdPct),
                ExpectedSpent = expected,
                PaceDeviation = deviation,
                PaceStatus = GetPaceStatus(deviation),
                Categories = rows
                    .Select(x => x.CategoryName)
                    .Distinct()
                    .ToList()
            });
        }

        var adherenceScore = CalculateBudgetAdherenceScore(buckets, plan.NeedsPct, plan.WantsPct);

        return new BucketTrackingResponse
        {
            PlanId = plan.PlanId,
            PlanName = plan.PlanName,
            StartDate = plan.StartDate,
            EndDate = plan.EndDate,
            MonthlyIncome = monthlyIncome,
            NeedsPct = plan.NeedsPct,
            WantsPct = plan.WantsPct,
            SavingsPct = plan.SavingsPct,
            Buckets = buckets,
            BudgetAdherenceScore = adherenceScore,
            UncategorizedRatio = uncategorizedRatio,
            UncategorizedWarning = uncategorizedRatio > 20m
        };
    }

    private decimal CalculateBudgetAdherenceScore(List<BucketBudgetResponse> buckets, decimal needsPct, decimal wantsPct)
    {
        var needs = buckets.FirstOrDefault(b => b.Bucket == "NEEDS");
        var wants = buckets.FirstOrDefault(b => b.Bucket == "WANTS");

        decimal weightSum = 0m;
        decimal weighted = 0m;

        if (needs is not null && needsPct > 0)
        {
            weighted += PacingScore(needs.PaceDeviation) * needsPct;
            weightSum += needsPct;
        }

        if (wants is not null && wantsPct > 0)
        {
            weighted += PacingScore(wants.PaceDeviation) * wantsPct;
            weightSum += wantsPct;
        }

        if (weightSum == 0m)
            return 100m;

        return Math.Round(weighted / weightSum, 2);
    }

    // Pacing -> điểm: Actual <= Expected (deviation <= 0) = 100đ.
    // Vượt 20% -> 80đ, vượt 50% -> 50đ, vượt 100% -> 0đ (tuyến tính).
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
        public Guid CategoryId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Total { get; set; }
    }

    private sealed record BudgetAlertPayload(
        string Title,
        string Message,
        Notification Notification);
}
