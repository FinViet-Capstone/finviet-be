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

    public async Task<IReadOnlyList<SavingGoalResponse>> GetGoalsAsync(
        Guid customerId,
        bool archived = false,
        CancellationToken cancellationToken = default)
    {
        var goals = await _dbContext.SavingGoals
            .AsNoTracking()
            .Where(g => g.CustomerId == customerId && g.IsDeleted == archived)
            .OrderBy(g => g.GoalName)
            .ToListAsync(cancellationToken);

        return goals.Select(ToResponse).ToList();
    }

    public async Task<SavingGoalResponse?> GetGoalByIdAsync(Guid customerId, Guid goalId, CancellationToken cancellationToken = default)
    {
        var goal = await _dbContext.SavingGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);

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
            IconEmoji = string.IsNullOrWhiteSpace(request.IconEmoji) ? null : request.IconEmoji.Trim(),
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

        if ((goal.CurrentAmount ?? 0m) != 0m)
        {
            throw new BusinessRuleException(
                "Withdraw the remaining saved amount before archiving this goal.",
                "goal_balance_must_be_withdrawn");
        }

        goal.IsDeleted = true;
        goal.UpdatedAt = DateTime.UtcNow;
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

        await ApplyContributionAsync(goal, customerId, request.Amount, cancellationToken, request.FundingWalletId, ValidateNote(request.Note));
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(goal);
        await IdempotencyStore.CompleteAsync(
            _dbContext, customerId, $"saving-goal-contribute:{goalId}", idempotencyKey!, response, cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        await NotifyMilestonesAsync(goal, previousAmount, goal.CurrentAmount ?? 0m, cancellationToken);
        return response;
    }

    public async Task<SavingGoalResponse?> WithdrawAsync(
        Guid customerId,
        Guid goalId,
        WithdrawSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new ValidationException("Withdrawal amount must be greater than zero.");
        var note = ValidateNote(request.Note);

        await using var databaseTransaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var requestHash = IdempotencyStore.ComputeRequestHash(new { goalId, request.Amount, request.WalletId, note });
        var idempotency = await IdempotencyStore.ClaimAsync(
            _dbContext, customerId, $"saving-goal-withdraw:{goalId}", idempotencyKey, requestHash, cancellationToken);
        if (idempotency.IsReplay)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
            return IdempotencyStore.ReadReplay<SavingGoalResponse?>(idempotency);
        }

        var goal = await LockGoalAsync(customerId, goalId, cancellationToken);
        if (goal is null)
            return null;

        var currentAmount = goal.CurrentAmount ?? 0m;
        if (request.Amount > currentAmount)
            throw new BusinessRuleException(
                "Cannot withdraw more than the goal's current saved amount.",
                "goal_withdraw_exceeds_saved");

        var wallet = (await LockWalletsAsync(new[] { request.WalletId }, cancellationToken)).SingleOrDefault();
        if (wallet is null || wallet.CustomerId != customerId || wallet.IsDeleted)
            throw new NotFoundException("Wallet not found.");
        if (string.Equals(wallet.WalletType, "sepay_linked", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "Withdrawals can only go to a regular wallet, not a bank-linked one.",
                "goal_withdraw_target_sepay_unsupported");

        var now = DateTime.UtcNow;
        var transaction = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = wallet.WalletId,
            CategoryId = GoalCategoryId,
            TransactionType = "income",
            EntryMethod = "manual",
            Amount = request.Amount,
            Description = $"Rút mục tiêu: {goal.GoalName}",
            TransactionDate = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        wallet.Balance = (wallet.Balance ?? 0m) + request.Amount;
        wallet.UpdatedAt = now;
        _dbContext.Transactions.Add(transaction);
        // Same ordering reason as ApplyContributionAsync: persist the ledger row before the
        // contribution row that FKs to it, so Postgres enforces the FK without relying on EF
        // batch ordering.
        await _dbContext.SaveChangesAsync(cancellationToken);

        goal.CurrentAmount = currentAmount - request.Amount;
        goal.IsCompleted = goal.CurrentAmount >= goal.TargetAmount;
        goal.UpdatedAt = now;
        _dbContext.SavingGoalContributions.Add(new SavingGoalContribution
        {
            ContributionId = Guid.NewGuid(),
            GoalId = goal.GoalId,
            TransactionId = transaction.TransactionId,
            Amount = request.Amount,
            Type = "withdrawal",
            Note = note,
            ContributionDate = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(goal);
        await IdempotencyStore.CompleteAsync(
            _dbContext, customerId, $"saving-goal-withdraw:{goalId}", idempotencyKey!, response, cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return response;
    }

    public async Task<IReadOnlyList<SavingGoalContributionResponse>?> GetContributionsAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        var goalExists = await _dbContext.SavingGoals
            .AsNoTracking()
            .AnyAsync(g => g.CustomerId == customerId && g.GoalId == goalId, cancellationToken);
        if (!goalExists)
            return null;

        var contributions = await _dbContext.SavingGoalContributions
            .AsNoTracking()
            .Where(c => c.GoalId == goalId)
            .OrderByDescending(c => c.ContributionDate)
            .ToListAsync(cancellationToken);

        return contributions.Select(c => new SavingGoalContributionResponse
        {
            ContributionId = c.ContributionId,
            GoalId = c.GoalId ?? goalId,
            Amount = c.Amount,
            Type = c.Type,
            ContributedAt = c.ContributionDate ?? DateTime.UtcNow,
            Note = c.Note,
            TransactionId = c.TransactionId
        }).ToList();
    }

    private async Task ApplyContributionAsync(
        SavingGoal goal,
        Guid customerId,
        decimal amount,
        CancellationToken cancellationToken,
        Guid? overrideWalletId = null,
        string? note = null)
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
            if (string.Equals(wallet.WalletType, "sepay_linked", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException(
                    "Contributions can only be funded from a regular wallet, not a bank-linked one.",
                    "goal_funding_wallet_sepay_unsupported");
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
            Type = "contribution",
            Note = note,
            ContributionDate = DateTime.UtcNow
        });
    }

    private const int MaxNoteLength = 255;

    internal static string? ValidateNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        var trimmed = note.Trim();
        if (trimmed.Length > MaxNoteLength)
            throw new ValidationException($"Note must not exceed {MaxNoteLength} characters.");

        return trimmed;
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

        var isEnabled = await _dbContext.CustomerSettings
            .AsNoTracking()
            .Where(setting => setting.CustomerId == goal.CustomerId.Value)
            .Select(setting => (bool?)setting.NotifGoals)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!isEnabled)
            return;

        var previousPct = previousAmount / goal.TargetAmount * 100m;
        var newPct = newAmount / goal.TargetAmount * 100m;
        foreach (var milestone in MilestonePercents.Where(m => previousPct < m && newPct >= m))
        {
            var title = milestone >= 100 ? "Hoàn thành mục tiêu tiết kiệm!" : $"Đạt {milestone}% mục tiêu tiết kiệm";
            var message = milestone >= 100
                ? $"Chúc mừng! Bạn đã hoàn thành mục tiêu \"{goal.GoalName}\"."
                : $"Mục tiêu \"{goal.GoalName}\" đã đạt {milestone}% ({newAmount:N0}/{goal.TargetAmount:N0}).";
            await _notificationService.NotifyAsync(
                goal.CustomerId.Value,
                "goal_milestone",
                title,
                message,
                "goal",
                goal.GoalId,
                cancellationToken);
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
            IconEmoji = goal.IconEmoji,
            TargetAmount = target,
            CurrentAmount = current,
            Deadline = goal.Deadline,
            FundingWalletId = goal.FundingWalletId,
            RemainingAmount = remaining,
            ProgressPercent = progressPercent,
            DaysRemaining = daysRemaining,
            IsCompleted = goal.IsCompleted || current >= target,
            IsDeleted = goal.IsDeleted,
            CreatedAt = goal.CreatedAt,
            UpdatedAt = goal.UpdatedAt,
            MonthlySavingNeeded = monthlySavingNeeded,
            MonthsRemaining = monthsRemaining
        };
    }
}
