namespace FinViet.Application.Interfaces;

/// <summary>Per-user AI call rate limiting backed by ai_usage_log.</summary>
public interface IAiRateLimiter
{
    /// <summary>Check the per-day and per-minute caps for the customer+feature. If under both,
    /// records a usage row and returns. If exceeded, throws (BadRequestException).
    /// <paramref name="feature"/> is one of CATEGORIZE/SCORE_COMMENT/REPORT/CHAT.</summary>
    Task CheckAndRecordAsync(Guid customerId, string feature, CancellationToken cancellationToken = default);
}
