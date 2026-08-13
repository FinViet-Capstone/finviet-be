namespace FinViet.Application.Interfaces;

/// <summary>Durable per-customer and per-feature AI rate limiter shared by all app instances.</summary>
public interface IAiRateLimiter
{
    Task<bool> TryAcquireAsync(
        Guid customerId,
        string feature,
        CancellationToken cancellationToken = default);
}
