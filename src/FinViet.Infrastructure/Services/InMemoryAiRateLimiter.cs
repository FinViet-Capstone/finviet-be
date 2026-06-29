using System.Collections.Concurrent;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Sliding-window per-customer AI rate limiter held entirely in process memory. Replaces the
/// durable ai_usage_log: simpler, no DB writes on the hot path. Tradeoff — counters reset on
/// restart and are not shared across instances; acceptable at MVP (single instance).
/// </summary>
public class InMemoryAiRateLimiter : IAiRateLimiter
{
    private readonly AiLimitsOptions _limits;

    // customerId -> recent call timestamps (UTC). Pruned on each call.
    private readonly ConcurrentDictionary<Guid, List<DateTime>> _calls = new();

    public InMemoryAiRateLimiter(IOptions<AiLimitsOptions> limits)
    {
        _limits = limits.Value;
    }

    public bool TryAcquire(Guid customerId, string feature)
    {
        var now = DateTime.UtcNow;
        var minuteAgo = now.AddMinutes(-1);
        var dayAgo = now.AddDays(-1);

        var window = _calls.GetOrAdd(customerId, _ => new List<DateTime>());

        lock (window)
        {
            window.RemoveAll(t => t < dayAgo);

            var inLastMinute = window.Count(t => t >= minuteAgo);
            var inLastDay = window.Count;

            if (inLastMinute >= _limits.PerUserPerMinute || inLastDay >= _limits.PerUserPerDay)
                return false;

            window.Add(now);
            return true;
        }
    }
}
