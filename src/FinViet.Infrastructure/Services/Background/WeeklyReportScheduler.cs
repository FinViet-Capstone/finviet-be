using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Generates weekly AI reports every Monday at 07:00 Asia/Ho_Chi_Minh for the just-completed week
/// (previous Mon–Sun). Runs each customer in its own DI scope with its own try/catch so one failure
/// doesn't abort the batch. Idempotent via the unique (customer, period_start) constraints, so a
/// restart mid-batch resumes safely. AI provider calls are spaced to avoid bursting rate limits.
/// </summary>
public class WeeklyReportScheduler : BackgroundService
{
    private static readonly TimeSpan PerCustomerDelay = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklyReportScheduler> _logger;
    private readonly TimeZoneInfo _vietnamTz;

    public WeeklyReportScheduler(IServiceScopeFactory scopeFactory, ILogger<WeeklyReportScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _vietnamTz = ResolveVietnamTimeZone();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogInformation("WeeklyReportScheduler: next run in {Delay}.", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WeeklyReportScheduler batch failed.");
            }
        }
    }

    private async Task RunBatchAsync(CancellationToken stoppingToken)
    {
        // Compute the previous completed week (Mon–Sun) in Vietnam local time.
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _vietnamTz);
        var todayVn = DateOnly.FromDateTime(nowVn);
        var thisMonday = StartOfWeek(todayVn);
        var weekStart = thisMonday.AddDays(-7);
        var weekEnd = thisMonday.AddDays(-1);

        List<Guid> customerIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
            customerIds = await db.Customers
                .Where(c => c.IsActive)
                .Select(c => c.CustomerId)
                .ToListAsync(stoppingToken);
        }

        _logger.LogInformation(
            "WeeklyReportScheduler: generating reports for {Count} customers, week {Start}–{End}.",
            customerIds.Count, weekStart, weekEnd);

        foreach (var customerId in customerIds)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reportService = scope.ServiceProvider.GetRequiredService<IWeeklyReportService>();
                await reportService.GenerateForWeekAsync(customerId, weekStart, weekEnd, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed generating weekly report for customer {CustomerId}.", customerId);
            }

            // Space out AI provider calls so a large batch doesn't hit RPM limits.
            try { await Task.Delay(PerCustomerDelay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _vietnamTz);

        // Next Monday 07:00 VN (strictly in the future).
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowVn.DayOfWeek + 7) % 7;
        var candidate = nowVn.Date.AddDays(daysUntilMonday).AddHours(7);
        if (candidate <= nowVn)
            candidate = candidate.AddDays(7);

        var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), _vietnamTz);
        var delay = candidateUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        int diff = ((int)d.DayOfWeek + 6) % 7; // Monday-based
        return d.AddDays(-diff);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Fallback: fixed +7 offset.
        return TimeZoneInfo.CreateCustomTimeZone("VN+7", TimeSpan.FromHours(7), "Vietnam (+7)", "Vietnam (+7)");
    }
}
