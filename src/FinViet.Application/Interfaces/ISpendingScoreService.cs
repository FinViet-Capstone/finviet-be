using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface ISpendingScoreService
{
    /// <summary>Compute a spending score for the given window. When <paramref name="persist"/> is
    /// true, the result is snapshotted to ai_spending_scores (idempotent on customer+type+start) —
    /// used by the Monday job for closed periods. Live/current-period calls pass false.</summary>
    Task<SpendingScoreResult> ComputeAsync(
        Guid customerId,
        string periodType,
        DateOnly periodStart,
        DateOnly periodEnd,
        bool persist,
        bool includeComment,
        CancellationToken cancellationToken = default);

    /// <summary>Compute the current (in-progress) period score live, without persisting — Home screen.</summary>
    Task<SpendingScoreResult> ComputeCurrentAsync(
        Guid customerId,
        string periodType,
        CancellationToken cancellationToken = default);
}
