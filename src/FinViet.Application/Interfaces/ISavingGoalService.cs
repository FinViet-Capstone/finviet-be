using FinViet.Application.DTOs.SavingGoals;

namespace FinViet.Application.Interfaces;

public interface ISavingGoalService
{
    Task<IReadOnlyList<SavingGoalResponse>> GetGoalsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<SavingGoalResponse?> GetGoalByIdAsync(
        Guid customerId,
        Guid goalId,
        CancellationToken cancellationToken = default);

    Task<SavingGoalResponse> CreateGoalAsync(
        Guid customerId,
        CreateSavingGoalRequest request,
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
        CancellationToken cancellationToken = default);
}
