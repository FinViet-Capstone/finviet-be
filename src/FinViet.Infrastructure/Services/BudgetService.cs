using System.ComponentModel.DataAnnotations;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class BudgetService : IBudgetService
{
    private const decimal DefaultThresholdPct = 80m;

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
            .Select(BuildBudgetPlanResponse)
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

        var hasOverlappingPlan = await _dbContext.BudgetPlans
            .AnyAsync(x =>
                x.CustomerId == customerId &&
                x.StartDate <= request.EndDate &&
                x.EndDate >= request.StartDate,
                cancellationToken);

        if (hasOverlappingPlan)
            throw new ValidationException("A budget plan already exists in this date range.");

        var categoryIds = request.CategoryBudgets
            .Select(x => x.CategoryId)
            .ToList();

        var distinctCategoryIds = categoryIds.Distinct().ToList();

        if (distinctCategoryIds.Count != categoryIds.Count)
            throw new ValidationException("Duplicate category budgets are not allowed.");

        var categories = await _dbContext.Categories
            .Where(x => distinctCategoryIds.Contains(x.CategoryId))
            .ToListAsync(cancellationToken);

        if (categories.Count != distinctCategoryIds.Count)
            throw new NotFoundException("Some categories do not exist.");

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
            ModelId = request.ModelId,
            PlanName = request.PlanName.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CategoryBudgets = request.CategoryBudgets.Select(x => new CategoryBudget
            {
                CategoryBudgetId = Guid.NewGuid(),
                CategoryId = x.CategoryId,
                AmountLimit = x.AmountLimit,
                CurrentSpent = 0m,
                ThresholdPct = x.ThresholdPct ?? DefaultThresholdPct,
                ThresholdType = string.IsNullOrWhiteSpace(x.ThresholdType)
                    ? "PERCENT"
                    : x.ThresholdType.Trim()
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

        return BuildCategoryBudgetResponse(categoryBudget);
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
            ModelId = lastPlan.ModelId,
            PlanName = $"Budget {today.Month}/{today.Year}",
            StartDate = currentMonthStart,
            EndDate = currentMonthEnd,
            CategoryBudgets = lastPlan.CategoryBudgets.Select(x => new CategoryBudget
            {
                CategoryBudgetId = Guid.NewGuid(),
                CategoryId = x.CategoryId,
                AmountLimit = x.AmountLimit,
                CurrentSpent = 0m,
                ThresholdPct = x.ThresholdPct ?? DefaultThresholdPct,
                ThresholdType = x.ThresholdType
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

    private async Task SyncCurrentSpentAsync(
        BudgetPlan plan,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var startUtc = plan.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endExclusiveUtc = plan.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var categoryIds = plan.CategoryBudgets
            .Where(x => x.CategoryId.HasValue)
            .Select(x => x.CategoryId!.Value)
            .Distinct()
            .ToList();

        var spentByCategory = categoryIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await (
                from transaction in _dbContext.Transactions.AsNoTracking()
                join wallet in _dbContext.Wallets.AsNoTracking()
                    on transaction.WalletId equals wallet.WalletId
                where transaction.CategoryId.HasValue
                      && categoryIds.Contains(transaction.CategoryId.Value)
                      && transaction.TransactionType == "EXPENSE"
                      && transaction.TransactionDate >= startUtc
                      && transaction.TransactionDate < endExclusiveUtc
                      && wallet.CustomerId == customerId
                group transaction by transaction.CategoryId!.Value into grouped
                select new
                {
                    CategoryId = grouped.Key,
                    Total = grouped.Sum(x => x.Amount)
                })
                .ToDictionaryAsync(x => x.CategoryId, x => x.Total, cancellationToken);

        var pendingAlerts = new List<BudgetAlertPayload>();

        foreach (var categoryBudget in plan.CategoryBudgets)
        {
            var previousSpent = categoryBudget.CurrentSpent ?? 0m;
            var threshold = categoryBudget.ThresholdPct ?? DefaultThresholdPct;
            var previousStatus = GetStatus(
                CalculatePercentage(previousSpent, categoryBudget.AmountLimit),
                threshold);

            var spent = categoryBudget.CategoryId.HasValue &&
                        spentByCategory.TryGetValue(categoryBudget.CategoryId.Value, out var total)
                ? total
                : 0m;

            categoryBudget.CurrentSpent = spent;

            var usedPercentage = CalculatePercentage(spent, categoryBudget.AmountLimit);
            var newStatus = GetStatus(usedPercentage, threshold);

            if (ShouldCreateBudgetAlert(previousStatus, newStatus))
            {
                var alert = CreateBudgetAlert(customerId, categoryBudget, usedPercentage, spent, newStatus);
                if (alert is not null)
                {
                    pendingAlerts.Add(alert);
                    _dbContext.Notifications.Add(alert.Notification);
                }
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
                .Select(BuildCategoryBudgetResponse)
                .ToList()
        };
    }

    private static BudgetTrackingResponse BuildTrackingResponse(BudgetPlan plan)
    {
        var categories = plan.CategoryBudgets
            .Select(BuildCategoryBudgetResponse)
            .ToList();

        var totalLimit = categories.Sum(x => x.AmountLimit);
        var totalSpent = categories.Sum(x => x.CurrentSpent);
        var usedPercentage = CalculatePercentage(totalSpent, totalLimit);

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
            Categories = categories
        };
    }

    private static CategoryBudgetResponse BuildCategoryBudgetResponse(CategoryBudget categoryBudget)
    {
        var currentSpent = categoryBudget.CurrentSpent ?? 0m;
        var threshold = categoryBudget.ThresholdPct ?? DefaultThresholdPct;
        var usedPercentage = CalculatePercentage(currentSpent, categoryBudget.AmountLimit);

        return new CategoryBudgetResponse
        {
            CategoryBudgetId = categoryBudget.CategoryBudgetId,
            CategoryId = categoryBudget.CategoryId,
            CategoryName = categoryBudget.Category?.CategoryName ?? string.Empty,
            AmountLimit = categoryBudget.AmountLimit,
            CurrentSpent = currentSpent,
            RemainingAmount = categoryBudget.AmountLimit - currentSpent,
            UsedPercentage = usedPercentage,
            ThresholdPct = categoryBudget.ThresholdPct,
            Status = GetStatus(usedPercentage, threshold)
        };
    }

    private static decimal CalculatePercentage(decimal spent, decimal limit)
    {
        if (limit <= 0)
            return 0m;

        return Math.Round(spent / limit * 100m, 2);
    }

    private static string GetStatus(decimal usedPercentage, decimal warningThreshold)
    {
        if (usedPercentage >= 100m)
            return "RED";

        if (usedPercentage >= warningThreshold)
            return "YELLOW";

        return "GREEN";
    }

    private static bool ShouldCreateBudgetAlert(string previousStatus, string newStatus)
        => !string.Equals(newStatus, "GREEN", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(previousStatus, newStatus, StringComparison.OrdinalIgnoreCase);

    private static BudgetAlertPayload? CreateBudgetAlert(
        Guid customerId,
        CategoryBudget categoryBudget,
        decimal usedPercentage,
        decimal spent,
        string status)
    {
        if (!categoryBudget.CategoryId.HasValue)
            return null;

        var categoryName = categoryBudget.Category?.CategoryName ?? "Budget category";
        var title = status == "RED"
            ? $"Budget exceeded: {categoryName}"
            : $"Budget warning: {categoryName}";
        var message = status == "RED"
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

    private sealed record BudgetAlertPayload(
        string Title,
        string Message,
        Notification Notification);
}
