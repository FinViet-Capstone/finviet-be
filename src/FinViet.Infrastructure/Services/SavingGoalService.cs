using FinViet.Application.DTOs.SavingGoals;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class SavingGoalService : ISavingGoalService
{
    // Milestone thresholds that trigger a push notification (function 45).
    private static readonly int[] MilestonePercents = { 25, 50, 75, 100 };

    private readonly FinVietDbContext _dbContext;
    private readonly INotificationService _notificationService;

    public SavingGoalService(FinVietDbContext dbContext, INotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<SavingGoalResponse>> GetGoalsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var goals = await _dbContext.SavingGoals
            .AsNoTracking()
            .Where(g => g.CustomerId == customerId)
            .OrderBy(g => g.GoalName)
            .ToListAsync(cancellationToken);

        return goals.Select(ToResponse).ToList();
    }

    public async Task<SavingGoalResponse?> GetGoalByIdAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);

        return goal is null ? null : ToResponse(goal);
    }

    public async Task<SavingGoalResponse> CreateGoalAsync(
        Guid customerId,
        CreateSavingGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.GoalName))
            throw new ValidationException("Goal name is required.");

        if (request.TargetAmount <= 0)
            throw new ValidationException("Target amount must be greater than zero.");

        if (request.Deadline.HasValue && request.Deadline.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Deadline must be in the future.");

        if (request.InitialAmount is < 0)
            throw new ValidationException("Initial amount cannot be negative.");

        var customerExists = await _dbContext.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!customerExists)
            throw new NotFoundException("Customer not found.");

        var trimmedName = request.GoalName.Trim();
        var duplicateName = await _dbContext.SavingGoals.AnyAsync(
            g => g.CustomerId == customerId && EF.Functions.ILike(g.GoalName, trimmedName),
            cancellationToken);

        if (duplicateName)
            throw new ValidationException("A saving goal with this name already exists.");

        var initial = request.InitialAmount ?? 0m;
        var goal = new SavingGoal
        {
            GoalId = Guid.NewGuid(),
            CustomerId = customerId,
            GoalName = trimmedName,
            TargetAmount = request.TargetAmount,
            CurrentAmount = initial,
            Deadline = request.Deadline
        };

        _dbContext.SavingGoals.Add(goal);

        if (initial > 0)
        {
            _dbContext.SavingGoalContributions.Add(new SavingGoalContribution
            {
                ContributionId = Guid.NewGuid(),
                GoalId = goal.GoalId,
                Amount = initial,
                ContributionDate = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(goal);
    }

    public async Task<SavingGoalResponse?> UpdateGoalAsync(
        Guid customerId,
        Guid goalId,
        UpdateSavingGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);

        if (goal is null)
            return null;

        if (request.GoalName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.GoalName))
                throw new ValidationException("Goal name cannot be empty.");

            var newName = request.GoalName.Trim();
            var duplicateName = await _dbContext.SavingGoals.AnyAsync(
                g => g.CustomerId == customerId && g.GoalId != goalId && EF.Functions.ILike(g.GoalName, newName),
                cancellationToken);

            if (duplicateName)
                throw new ValidationException("A saving goal with this name already exists.");

            goal.GoalName = newName;
        }

        if (request.TargetAmount.HasValue)
        {
            if (request.TargetAmount.Value <= 0)
                throw new ValidationException("Target amount must be greater than zero.");

            if (request.TargetAmount.Value < (goal.CurrentAmount ?? 0m))
                throw new ValidationException("Target amount cannot be less than the amount already saved.");

            goal.TargetAmount = request.TargetAmount.Value;
        }

        if (request.Deadline.HasValue)
        {
            if (request.Deadline.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ValidationException("Deadline must be in the future.");

            goal.Deadline = request.Deadline.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(goal);
    }

    public async Task<bool> DeleteGoalAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);

        if (goal is null)
            return false;

        _dbContext.SavingGoals.Remove(goal);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SavingGoalResponse?> ContributeAsync(
        Guid customerId,
        Guid goalId,
        ContributeSavingGoalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ValidationException("Contribution amount must be greater than zero.");

        var goal = await _dbContext.SavingGoals
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);

        if (goal is null)
            return null;

        var previousAmount = goal.CurrentAmount ?? 0m;
        var newAmount = previousAmount + request.Amount;

        goal.CurrentAmount = newAmount;
        _dbContext.SavingGoalContributions.Add(new SavingGoalContribution
        {
            ContributionId = Guid.NewGuid(),
            GoalId = goal.GoalId,
            Amount = request.Amount,
            ContributionDate = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotifyMilestonesAsync(goal, previousAmount, newAmount, cancellationToken);

        return ToResponse(goal);
    }

    /// <summary>
    /// Fires a notification for each milestone (25/50/75/100%) newly crossed by this contribution.
    /// </summary>
    private async Task NotifyMilestonesAsync(
        SavingGoal goal,
        decimal previousAmount,
        decimal newAmount,
        CancellationToken cancellationToken)
    {
        if (goal.TargetAmount <= 0 || goal.CustomerId is null)
            return;

        var previousPct = previousAmount / goal.TargetAmount * 100m;
        var newPct = newAmount / goal.TargetAmount * 100m;

        foreach (var milestone in MilestonePercents)
        {
            // Crossed this milestone if we were below it before and are at/above it now.
            if (previousPct < milestone && newPct >= milestone)
            {
                var title = milestone >= 100
                    ? "Hoàn thành mục tiêu tiết kiệm!"
                    : $"Đạt {milestone}% mục tiêu tiết kiệm";

                var message = milestone >= 100
                    ? $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.GoalName}\"."
                    : $"Mục tiêu \"{goal.GoalName}\" đã đạt {milestone}% ({newAmount:N0}/{goal.TargetAmount:N0}).";

                await _notificationService.NotifyAsync(
                    goal.CustomerId.Value,
                    title,
                    message,
                    goal.GoalId,
                    cancellationToken);
            }
        }
    }

    private static SavingGoalResponse ToResponse(SavingGoal goal)
    {
        var target = goal.TargetAmount;
        var current = goal.CurrentAmount ?? 0m;
        var remaining = Math.Max(0m, target - current);

        var progressPercent = target > 0
            ? Math.Round(Math.Min(100m, current / target * 100m), 2)
            : 0m;

        int? daysRemaining = null;
        int? monthsRemaining = null;
        decimal? monthlySavingNeeded = null;

        if (goal.Deadline.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            daysRemaining = Math.Max(0, goal.Deadline.Value.DayNumber - today.DayNumber);

            // Whole months left (at least 1 if the deadline is still in the future), used to spread the remaining amount.
            var months = (goal.Deadline.Value.Year - today.Year) * 12 + (goal.Deadline.Value.Month - today.Month);
            if (goal.Deadline.Value.Day >= today.Day)
                months += 0;
            else
                months -= 1;

            monthsRemaining = Math.Max(0, months);

            if (remaining <= 0)
                monthlySavingNeeded = 0m;
            else if (monthsRemaining >= 1)
                monthlySavingNeeded = Math.Round(remaining / monthsRemaining.Value, 2);
            else
                // Deadline is within the current month — the whole remainder is needed now.
                monthlySavingNeeded = remaining;
        }

        return new SavingGoalResponse
        {
            GoalId = goal.GoalId,
            CustomerId = goal.CustomerId ?? Guid.Empty,
            GoalName = goal.GoalName,
            TargetAmount = target,
            CurrentAmount = current,
            Deadline = goal.Deadline,
            RemainingAmount = remaining,
            ProgressPercent = progressPercent,
            DaysRemaining = daysRemaining,
            IsCompleted = current >= target,
            MonthlySavingNeeded = monthlySavingNeeded,
            MonthsRemaining = monthsRemaining
        };
    }
}
