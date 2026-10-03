using FinViet.Infrastructure.Services.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Runs the subscription lifecycle sweep (expiry + resubscribe reminders, finviet-be#138) shortly
/// after startup and then every <see cref="SubscriptionLifecycleOptions.SweepIntervalMinutes"/>.
/// It deliberately does not wait for a fixed time of day: the API host can be asleep at any given
/// moment, and the sweep is idempotent, so a missed slot is simply caught up by the next run.
/// </summary>
public class SubscriptionLifecycleScheduler : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubscriptionLifecycleOptions _options;
    private readonly ILogger<SubscriptionLifecycleScheduler> _logger;

    public SubscriptionLifecycleScheduler(
        IServiceScopeFactory scopeFactory,
        IOptions<SubscriptionLifecycleOptions> options,
        ILogger<SubscriptionLifecycleScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
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
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISubscriptionLifecycleService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionLifecycleScheduler sweep failed.");
            }

            delay = TimeSpan.FromMinutes(_options.SweepIntervalMinutes);
        }
    }
}
