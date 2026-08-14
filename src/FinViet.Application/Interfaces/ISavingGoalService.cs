using FinViet.Application.DTOs.SavingGoals;

namespace FinViet.Application.Interfaces;

public interface ISavingGoalService
{
    Task<IReadOnlyList<SavingGoalResponse>> GetGoalsAsync(
        Guid customerId,
        bool archived = false,
        CancellationToken cancellationToken = default);

    Task<SavingGoalResponse?> GetGoalByIdAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default);

    Task<SavingGoalResponse> CreateGoalAsync(
        Guid customerId,
        CreateSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SavingGoalResponse?> UpdateGoalAsync(
        Guid customerId,
        Guid goalId,
        UpdateSavingGoalRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteGoalAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds money to a goal, updates progress, and fires milestone notifications.</summary>
    Task<SavingGoalResponse?> ContributeAsync(
        Guid customerId,
        Guid goalId,
        ContributeSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Moves money from a goal back to a regular wallet. Null return means the goal was not found/owned.</summary>
    Task<SavingGoalResponse?> WithdrawAsync(
        Guid customerId,
        Guid goalId,
        WithdrawSavingGoalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Full contribution/withdrawal ledger for a goal, newest first. Null means the goal was not found/owned.</summary>
    Task<IReadOnlyList<SavingGoalContributionResponse>?> GetContributionsAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default);
}
