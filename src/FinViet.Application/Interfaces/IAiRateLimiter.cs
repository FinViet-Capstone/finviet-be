namespace FinViet.Application.Interfaces;

/// <summary>In-memory per-customer rate limiter for AI calls (sliding window), replacing
/// the durable ai_usage_log. Resets on restart and is single-instance only — acceptable
/// at MVP. Returns false when the customer has exceeded the per-minute or per-day limit.</summary>
public interface IAiRateLimiter
{
    bool TryAcquire(Guid customerId, string feature);
}
