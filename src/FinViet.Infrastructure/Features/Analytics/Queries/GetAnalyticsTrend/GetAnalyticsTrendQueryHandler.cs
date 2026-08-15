using FinViet.Application.DTOs.Analytics;
using FinViet.Application.Features.Analytics.Queries.GetAnalyticsTrend;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Analytics.Queries.GetAnalyticsTrend;

public class GetAnalyticsTrendQueryHandler : IRequestHandler<GetAnalyticsTrendQuery, List<DailyMetricDto>>
{
    private readonly FinVietDbContext _db;
    public GetAnalyticsTrendQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<List<DailyMetricDto>> Handle(GetAnalyticsTrendQuery request, CancellationToken cancellationToken)
    {
        var days = request.Days is < 1 or > 365 ? 30 : request.Days;
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        Dictionary<DateTime, int> countsByDay;
        if (request.Metric == "transactions")
        {
            countsByDay = await _db.Transactions.AsNoTracking()
                .Where(t => t.CreatedAt >= since)
                .GroupBy(t => t.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Day, g => g.Count, cancellationToken);
        }
        else
        {
            // Default/unrecognized metric falls through to signups (lenient default).
            countsByDay = await _db.Customers.AsNoTracking()
                .Where(c => c.DeletedAt == null && c.CreatedAt != null && c.CreatedAt >= since)
                .GroupBy(c => c.CreatedAt!.Value.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Day, g => g.Count, cancellationToken);
        }

        // Zero-fill every day in range so the frontend chart never has gaps.
        return Enumerable.Range(0, days)
            .Select(offset => since.AddDays(offset))
            .Select(day => new DailyMetricDto
            {
                Date = day.ToString("yyyy-MM-dd"),
                Count = countsByDay.GetValueOrDefault(day, 0),
            })
            .ToList();
    }
}
