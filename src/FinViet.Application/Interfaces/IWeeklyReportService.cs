using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IWeeklyReportService
{
    /// <summary>Generate (or return existing) the weekly report for the given customer and the
    /// completed week ending <paramref name="weekEnd"/>. Idempotent per (customer, week start).
    /// Computes + snapshots the weekly score, generates the Vietnamese narrative, persists, and
    /// pushes a notification. Returns null if there was no activity worth reporting and skipReason set.</summary>
    Task<WeeklyReportResponse> GenerateForWeekAsync(
        Guid customerId,
        DateOnly weekStart,
        DateOnly weekEnd,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeeklyReportResponse>> GetHistoryAsync(
        Guid customerId, CancellationToken cancellationToken = default);

    Task<WeeklyReportResponse?> GetByIdAsync(
        Guid customerId, Guid reportId, CancellationToken cancellationToken = default);
}
