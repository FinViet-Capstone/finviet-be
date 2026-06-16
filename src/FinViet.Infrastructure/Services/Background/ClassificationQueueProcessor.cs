using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Drains the ai_classification_queue. Wakes on a channel signal (fast path) and also polls
/// periodically to recover rows left PENDING/PROCESSING after a restart or Gemini outage.
/// Each work unit runs in its own DI scope (DbContext + categorization service are scoped).
/// </summary>
public class ClassificationQueueProcessor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private const int MaxAttempts = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClassificationQueueSignal _signal;
    private readonly ILogger<ClassificationQueueProcessor> _logger;

    public ClassificationQueueProcessor(
        IServiceScopeFactory scopeFactory,
        ClassificationQueueSignal signal,
        ILogger<ClassificationQueueProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Drain any backlog left from a previous run before listening for new signals.
        await DrainDueItemsAsync(stoppingToken);

        var signalTask = ConsumeSignalsAsync(stoppingToken);
        var pollTask = PollLoopAsync(stoppingToken);
        await Task.WhenAll(signalTask, pollTask);
    }

    private async Task ConsumeSignalsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var _ in _signal.ReadAllAsync(stoppingToken))
            {
                await DrainDueItemsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task PollLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, stoppingToken);
                await DrainDueItemsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task DrainDueItemsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
            var categorizer = scope.ServiceProvider.GetRequiredService<IAiCategorizationService>();

            var now = DateTime.UtcNow;
            var due = await db.AiClassificationQueueItems
                .Where(q => (q.Status == "PENDING" || q.Status == "PROCESSING")
                            && (q.NextAttemptAt == null || q.NextAttemptAt <= now))
                .OrderBy(q => q.EnqueuedAt)
                .Take(25)
                .ToListAsync(stoppingToken);

            foreach (var item in due)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var ok = await categorizer.ReprocessAsync(item.TransactionId, item.RawInput, stoppingToken);
                item.AttemptCount++;

                if (ok)
                {
                    item.Status = "DONE";
                    item.ProcessedAt = DateTime.UtcNow;
                    item.LastError = null;
                }
                else if (item.AttemptCount >= MaxAttempts)
                {
                    item.Status = "FAILED";
                    item.LastError = "Gemini unavailable after maximum retries.";
                    item.ProcessedAt = DateTime.UtcNow;
                }
                else
                {
                    item.Status = "PENDING";
                    item.LastError = "Gemini unavailable; will retry.";
                    // Exponential backoff capped at 30 minutes.
                    var delayMinutes = Math.Min(30, (int)Math.Pow(2, item.AttemptCount));
                    item.NextAttemptAt = DateTime.UtcNow.AddMinutes(delayMinutes);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error draining classification queue.");
        }
    }
}
