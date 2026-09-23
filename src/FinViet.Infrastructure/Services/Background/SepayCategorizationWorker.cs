using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Drains <see cref="SepayCategorizationQueue"/> one customer batch at a time and categorizes it
/// through <see cref="IAiCategorizationService.CategorizeManyAsync"/>, keeping Gemini calls off the
/// link, sync and webhook requests. A failing batch is logged and never stops the worker; its rows
/// stay queued-looking until the pending window lapses and they show as failed.
/// </summary>
public sealed class SepayCategorizationWorker : BackgroundService
{
    private readonly SepayCategorizationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SepayCategorizationWorker> _logger;

    public SepayCategorizationWorker(
        SepayCategorizationQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<SepayCategorizationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var batch in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "SePay categorization batch of {Count} rows failed for customer {CustomerId}.",
                        batch.TransactionIds.Count,
                        batch.CustomerId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task ProcessBatchAsync(SepayCategorizationBatch batch, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var categorization = scope.ServiceProvider.GetRequiredService<IAiCategorizationService>();
        var outcomes = await categorization.CategorizeManyAsync(
            batch.CustomerId, batch.TransactionIds, cancellationToken);

        var appliedIds = outcomes.Where(o => o.Applied).Select(o => o.TransactionId).ToList();
        if (appliedIds.Count == 0)
            return;

        // Budgets only count categorized rows, so re-check once per affected month.
        var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
        var dates = await db.Transactions.AsNoTracking()
            .Where(t => appliedIds.Contains(t.TransactionId))
            .Select(t => t.TransactionDate)
            .ToListAsync(cancellationToken);
        var budgets = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        foreach (var month in dates.OfType<DateTime>().Select(SepayCategorizationWindow.MonthOf).Distinct())
            await budgets.SyncBudgetOnTransactionChangeAsync(batch.CustomerId, month, cancellationToken);
    }
}
