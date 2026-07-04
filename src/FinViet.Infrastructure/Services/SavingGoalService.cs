using System.Data;
using FinViet.Application.DTOs.SavingGoals;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Idempotency;
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = FinViet.Application.Common.Exceptions.BusinessRuleException;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;
using ValidationException = FinViet.Application.Exceptions.ValidationException;

namespace FinViet.Infrastructure.Services;

public class SavingGoalService : ISavingGoalService
{
    private static readonly int[] MilestonePercents = { 25, 50, 75, 100 };
    private const string GoalCategoryId = "cat_savings_goal";

    private readonly FinVietDbContext _dbContext;
    private readonly INotificationService _notificationService;

    public SavingGoalService(FinVietDbContext dbContext, INotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<SavingGoalResponse>> GetGoalsAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var goals = await _dbContext.SavingGoals
            .AsNoTracking()
            .Where(g => g.CustomerId == customerId && !g.IsDeleted)
            .OrderBy(g => g.GoalName)
            .ToListAsync(cancellationToken);

        return goals.Select(ToResponse).ToList();
    }

    public async Task<SavingGoalResponse?> GetGoalByIdAsync(Guid customerId, Guid goalId, CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId && !g.IsDeleted, cancellationToken);

        return goal is null ? null : ToResponse(goal);
    }

    public async Task<SavingGoalResponse> CreateGoalAsync(
        Guid customerId,
        CreateSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateCreate(request);

        var customerExists = await _dbContext.Customers.AnyAsync(c => c.CustomerId == customerId, cancellationToken);
        if (!customerExists)
            throw new NotFoundException("Customer not found.");

        await using var databaseTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var requestHash = IdempotencyStore.ComputeRequestHash(request);
        var idempotency = await IdempotencyStore.ClaimAsync(
            _dbContext, customerId, "saving-goal-create", idempotencyKey, requestHash, cancellationToken);
        if (idempotency.IsReplay)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
            return IdempotencyStore.ReadReplay<SavingGoalResponse>(idempotency);
        }

        var trimmedName = request.GoalName.Trim();
        var duplicateName = await _dbContext.SavingGoals.AnyAsync(
            g => g.CustomerId == customerId && !g.IsDeleted && EF.Functions.ILike(g.GoalName, trimmedName),
            cancellationToken);
        if (duplicateName)
            throw new ValidationException("A saving goal with this name already exists.");

        var initialAmount = request.InitialAmount ?? 0m;
        var goal = new SavingGoal
        {
            GoalId = Guid.NewGuid(),
            CustomerId = customerId,
            GoalName = trimmedName,
            TargetAmount = request.TargetAmount,
            CurrentAmount = 0m,
            Deadline = request.Deadline,
            FundingWalletId = request.FundingWalletId,
            IsCompleted = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (goal.FundingWalletId.HasValue)
        {
            var fundingWallet = (await LockWalletsAsync(new[] { goal.FundingWalletId.Value }, cancellationToken)).SingleOrDefault();
            if (fundingWallet is null || fundingWallet.CustomerId != customerId || fundingWallet.IsDeleted)
                throw new NotFoundException("Funding wallet not found.");
        }

        _dbContext.SavingGoals.Add(goal);
        if (initialAmount > 0)
            await ApplyContributionAsync(goal, customerId, initialAmount, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(goal);
        await IdempotencyStore.CompleteAsync(
            _dbContext, customerId, "saving-goal-create", idempotencyKey!, response, cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        if (initialAmount > 0)
            await NotifyMilestonesAsync(goal, 0m, initialAmount, cancellationToken);

        return response;
    }

    public async Task<SavingGoalResponse?> UpdateGoalAsync(Guid customerId, Guid goalId, UpdateSavingGoalRequest request, CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId && !g.IsDeleted, cancellationToken);
        if (goal is null)
            return null;

        if (request.GoalName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.GoalName))
                throw new ValidationException("Goal name cannot be empty.");

            var newName = request.GoalName.Trim();
            var duplicateName = await _dbContext.SavingGoals.AnyAsync(
                g => g.CustomerId == customerId && g.GoalId != goalId && !g.IsDeleted && EF.Functions.ILike(g.GoalName, newName),
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
            goal.IsCompleted = (goal.CurrentAmount ?? 0m) >= goal.TargetAmount;
        }

        if (request.Deadline.HasValue)
        {
            if (request.Deadline.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ValidationException("Deadline must be in the future.");
            goal.Deadline = request.Deadline.Value;
        }

        goal.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(goal);
    }

    public async Task<bool> DeleteGoalAsync(Guid customerId, Guid goalId, CancellationToken cancellationToken = default)
    {
        await using var databaseTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var goal = await LockGoalAsync(customerId, goalId, cancellationToken);
        if (goal is null)
            return false;

        var contributions = await _dbContext.SavingGoalContributions
            .Where(c => c.GoalId == goalId)
            .ToListAsync(cancellationToken);
        var transactionIds = contributions
            .Where(c => c.TransactionId.HasValue)
            .Select(c => c.TransactionId!.Value)
            .Distinct()
            .ToArray();
        var generatedTransactions = transactionIds.Length == 0
            ? new List<Transaction>()
            : await _dbContext.Transactions.Where(t => transactionIds.Contains(t.TransactionId)).ToListAsync(cancellationToken);

        if (generatedTransactions.Count != transactionIds.Length
            || generatedTransactions.Any(t => t.CustomerId != customerId || t.TransactionType != "expense" || t.CategoryId != GoalCategoryId))
        {
            throw new BusinessRuleException("Goal contribution ledger is inconsistent and cannot be reversed safely.", "goal_ledger_invalid");
        }

        var wallets = await LockWalletsAsync(generatedTransactions.Select(t => t.WalletId).Distinct().ToArray(), cancellationToken);
        if (wallets.Count != generatedTransactions.Select(t => t.WalletId).Distinct().Count()
            || wallets.Any(w => w.CustomerId != customerId))
        {
            throw new BusinessRuleException("Funding wallet is no longer available for goal reversal.", "goal_wallet_missing");
        }

        var walletsById = wallets.ToDictionary(w => w.WalletId);
        foreach (var transaction in generatedTransactions)
        {
            var wallet = walletsById[transaction.WalletId];
            wallet.Balance = (wallet.Balance ?? 0m) + transaction.Amount;
            wallet.UpdatedAt = DateTime.UtcNow;
        }

        _dbContext.Transactions.RemoveRange(generatedTransactions);
        _dbContext.SavingGoalContributions.RemoveRange(contributions);
        _dbContext.SavingGoals.Remove(goal);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<SavingGoalResponse?> ContributeAsync(
        Guid customerId,
        Guid goalId,
        ContributeSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ValidationException("Contribution amount must be greater than zero.");

        await using var databaseTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var requestHash = IdempotencyStore.ComputeRequestHash(new { goalId, request.Amount, request.FundingWalletId });
        var idempotency = await IdempotencyStore.ClaimAsync(
            _dbContext, customerId, $"saving-goal-contribute:{goalId}", idempotencyKey, requestHash, cancellationToken);
        if (idempotency.IsReplay)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
            return IdempotencyStore.ReadReplay<SavingGoalResponse?>(idempotency);
        }

        var goal = await LockGoalAsync(customerId, goalId, cancellationToken);
        if (goal is null)
            return null;

        var previousAmount = goal.CurrentAmount ?? 0m;
        if (request.Amount > goal.TargetAmount - previousAmount)
            throw new BusinessRuleException("Contribution amount exceeds the remaining goal amount.", "goal_remaining_exceeded");

        await ApplyContributionAsync(goal, customerId, request.Amount, cancellationToken, request.FundingWalletId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(goal);
        await IdempotencyStore.CompleteAsync(
            _dbContext, customerId, $"saving-goal-contribute:{goalId}", idempotencyKey!, response, cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        await NotifyMilestonesAsync(goal, previousAmount, goal.CurrentAmount ?? 0m, cancellationToken);
        return response;
    }

    private async Task ApplyContributionAsync(
        SavingGoal goal,
        Guid customerId,
        decimal amount,
        CancellationToken cancellationToken,
        Guid? overrideWalletId = null)
    {
        var previousAmount = goal.CurrentAmount ?? 0m;
        if (amount > goal.TargetAmount - previousAmount)
            throw new BusinessRuleException("Contribution amount exceeds the remaining goal amount.", "goal_remaining_exceeded");

        // Prefer the wallet chosen for this contribution; fall back to the goal's funding wallet.
        var sourceWalletId = overrideWalletId ?? goal.FundingWalletId;

        Guid? transactionId = null;
        if (sourceWalletId.HasValue)
        {
            var wallet = (await LockWalletsAsync(new[] { sourceWalletId.Value }, cancellationToken)).SingleOrDefault();
            if (wallet is null || wallet.CustomerId != customerId || wallet.IsDeleted)
                throw new NotFoundException("Funding wallet not found.");
            if ((wallet.Balance ?? 0m) < amount)
                throw new BusinessRuleException("Funding wallet does not have enough balance.", "insufficient_balance");

            var now = DateTime.UtcNow;
            var transaction = new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = customerId,
                WalletId = wallet.WalletId,
                CategoryId = GoalCategoryId,
                TransactionType = "expense",
                EntryMethod = "manual",
                Amount = amount,
                Description = $"Nạp mục tiêu: {goal.GoalName}",
                TransactionDate = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            wallet.Balance = (wallet.Balance ?? 0m) - amount;
            wallet.UpdatedAt = now;
            _dbContext.Transactions.Add(transaction);
            // The contribution has a database FK to transactions but no CLR navigation
            // property. Persist the ledger row first so PostgreSQL can enforce that FK
            // without relying on EF batch ordering.
            await _dbContext.SaveChangesAsync(cancellationToken);
            transactionId = transaction.TransactionId;
        }

        goal.CurrentAmount = previousAmount + amount;
        goal.IsCompleted = goal.CurrentAmount >= goal.TargetAmount;
        goal.UpdatedAt = DateTime.UtcNow;
        _dbContext.SavingGoalContributions.Add(new SavingGoalContribution
        {
            ContributionId = Guid.NewGuid(),
            GoalId = goal.GoalId,
            TransactionId = transactionId,
            Amount = amount,
            ContributionDate = DateTime.UtcNow
        });
    }

    private async Task<SavingGoal?> LockGoalAsync(Guid customerId, Guid goalId, CancellationToken cancellationToken)
        => await _dbContext.SavingGoals
            .FromSqlInterpolated($"""
                SELECT id, customer_id, name, icon_emoji, target_amount, current_amount,
                       deadline, funding_wallet_id, is_completed, is_deleted, created_at, updated_at
                FROM savings_goals
                WHERE id = {goalId} AND customer_id = {customerId} AND is_deleted = false
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<List<Wallet>> LockWalletsAsync(Guid[] walletIds, CancellationToken cancellationToken)
    {
        if (walletIds.Length == 0)
            return new List<Wallet>();

        return await _dbContext.Wallets
            .FromSqlInterpolated($"""
                SELECT id, customer_id, name, type, balance, is_deleted, created_at, updated_at
                FROM wallets
                WHERE id = ANY({walletIds})
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    private static void ValidateCreate(CreateSavingGoalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GoalName))
            throw new ValidationException("Goal name is required.");
        if (request.TargetAmount <= 0)
            throw new ValidationException("Target amount must be greater than zero.");
        if (!request.Deadline.HasValue)
            throw new ValidationException("Deadline is required.");
        if (request.Deadline.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException("Deadline must be in the future.");
        if (request.InitialAmount is < 0)
            throw new ValidationException("Initial amount cannot be negative.");
        if (request.InitialAmount.GetValueOrDefault() > request.TargetAmount)
            throw new ValidationException("Initial amount cannot exceed the target amount.");
    }

    private async Task NotifyMilestonesAsync(SavingGoal goal, decimal previousAmount, decimal newAmount, CancellationToken cancellationToken)
    {
        if (goal.TargetAmount <= 0 || goal.CustomerId is null)
            return;

        var previousPct = previousAmount / goal.TargetAmount * 100m;
        var newPct = newAmount / goal.TargetAmount * 100m;
        foreach (var milestone in MilestonePercents.Where(m => previousPct < m && newPct >= m))
        {
            var title = milestone >= 100 ? "Hoàn thành mục tiêu tiết kiệm!" : $"Đạt {milestone}% mục tiêu tiết kiệm";
            var message = milestone >= 100
                ? $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.GoalName}\"."
                : $"Mục tiêu \"{goal.GoalName}\" đã đạt {milestone}% ({newAmount:N0}/{goal.TargetAmount:N0}).";
            await _notificationService.NotifyAsync(goal.CustomerId.Value, title, message, goal.GoalId, cancellationToken);
        }
    }

    private static SavingGoalResponse ToResponse(SavingGoal goal)
    {
        var target = goal.TargetAmount;
        var current = goal.CurrentAmount ?? 0m;
        var remaining = Math.Max(0m, target - current);
        var progressPercent = target > 0 ? Math.Round(Math.Min(100m, current / target * 100m), 2) : 0m;

        int? daysRemaining = null;
        int? monthsRemaining = null;
        decimal? monthlySavingNeeded = null;
        if (goal.Deadline.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            daysRemaining = Math.Max(0, goal.Deadline.Value.DayNumber - today.DayNumber);
            var months = (goal.Deadline.Value.Year - today.Year) * 12 + (goal.Deadline.Value.Month - today.Month);
            if (goal.Deadline.Value.Day < today.Day)
                months--;
            monthsRemaining = Math.Max(0, months);
            monthlySavingNeeded = remaining <= 0 ? 0m : monthsRemaining >= 1
                ? Math.Round(remaining / monthsRemaining.Value, 2)
                : remaining;
        }

        return new SavingGoalResponse
        {
            GoalId = goal.GoalId,
            CustomerId = goal.CustomerId ?? Guid.Empty,
            GoalName = goal.GoalName,
            TargetAmount = target,
            CurrentAmount = current,
            Deadline = goal.Deadline,
            FundingWalletId = goal.FundingWalletId,
            RemainingAmount = remaining,
            ProgressPercent = progressPercent,
            DaysRemaining = daysRemaining,
            IsCompleted = goal.IsCompleted || current >= target,
            MonthlySavingNeeded = monthlySavingNeeded,
            MonthsRemaining = monthsRemaining
        };
    }
}
