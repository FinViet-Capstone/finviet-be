using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>
/// Persists privacy-safe AI operational metadata. Implementations must be best-effort and must never
/// throw into the customer operation being observed.
/// </summary>
public interface IAiTelemetryRecorder
{
    Task RecordUsageAsync(
        AiUsageRecord record,
        CancellationToken cancellationToken = default);

    Task RecordAuditAsync(
        AiAuditRecord record,
        CancellationToken cancellationToken = default);
}
