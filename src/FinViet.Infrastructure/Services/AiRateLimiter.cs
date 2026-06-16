using FinViet.Application.Common.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// DB-backed per-user rate limiter. Counts ai_usage_log rows for the current day and the last
/// minute against the configured caps, then records a row. The count-then-insert is not atomic;
/// slight overage under heavy concurrency is acceptable for this app's volume.
/// </summary>
public class AiRateLimiter : IAiRateLimiter
{
    private readonly FinVietDbContext _db;
    private readonly AiLimitsOptions _limits;

    public AiRateLimiter(FinVietDbContext db, IOptions<AiLimitsOptions> limits)
    {
        _db = db;
        _limits = limits.Value;
    }

    public async Task CheckAndRecordAsync(Guid customerId, string feature, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var minuteAgo = now.AddMinutes(-1);

        var dayCount = await _db.AiUsageLogs
            .CountAsync(u => u.CustomerId == customerId && u.CalledAt >= dayStart, cancellationToken);

        if (dayCount >= _limits.PerUserPerDay)
            throw new BadRequestException(
                $"Đã đạt giới hạn AI trong ngày ({_limits.PerUserPerDay} lượt). Vui lòng thử lại vào ngày mai.");

        var minuteCount = await _db.AiUsageLogs
            .CountAsync(u => u.CustomerId == customerId && u.CalledAt >= minuteAgo, cancellationToken);

        if (minuteCount >= _limits.PerUserPerMinute)
            throw new BadRequestException(
                $"Bạn thao tác quá nhanh ({_limits.PerUserPerMinute} lượt/phút). Vui lòng đợi một chút.");

        _db.AiUsageLogs.Add(new AiUsageLog
        {
            UsageId = Guid.NewGuid(),
            CustomerId = customerId,
            Feature = feature,
            CalledAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
