using System.Globalization;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Services.Subscriptions;

/// <summary>
/// One sweep of the subscription lifecycle (finviet-be#138): marks active subscriptions whose paid
/// period is over as expired, and sends each resubscribe reminder (before, on, and after the end
/// date) at most once per paid period. Idempotent, so it is safe to run as often as needed.
/// </summary>
internal interface ISubscriptionLifecycleService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class SubscriptionLifecycleService : ISubscriptionLifecycleService
{
    internal const string EndingSoon = "ending_soon";
    internal const string EndDay = "end_day";
    internal const string ExpiredKind = "expired";

    // The mobile app files 'announcement' + 'system' under its "Hệ thống" notifications tab.
    private const string NotificationType = "announcement";
    private const string NotificationEntityType = "system";

    private readonly FinVietDbContext _db;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _time;
    private readonly SubscriptionLifecycleOptions _options;
    private readonly ILogger<SubscriptionLifecycleService> _logger;

    public SubscriptionLifecycleService(
        FinVietDbContext db,
        INotificationService notifications,
        TimeProvider time,
        IOptions<SubscriptionLifecycleOptions> options,
        ILogger<SubscriptionLifecycleService> logger)
    {
        _db = db;
        _notifications = notifications;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = VietnamClock.Now(_time);
        var today = DateOnly.FromDateTime(now);

        await ExpireLapsedAsync(today, cancellationToken);

        if (now.Hour >= _options.ReminderStartHour && now.Hour < _options.ReminderEndHour)
            await SendRemindersAsync(today, cancellationToken);
    }

    private async Task ExpireLapsedAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var lapsed = await _db.CustomerSubscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionPeriods.Active && s.EndDate != null && s.EndDate < today)
            .Select(s => new { s.SubscriptionId, s.CustomerId })
            .ToListAsync(cancellationToken);

        foreach (var row in lapsed)
        {
            try
            {
                await ExpireAsync(row.SubscriptionId, row.CustomerId, today, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to expire subscription {SubscriptionId}.", row.SubscriptionId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task ExpireAsync(Guid subscriptionId, Guid? customerId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // A payment for this customer may be extending the subscription right now: take the same
        // lock it does and re-check, so an extension is never overwritten with "expired".
        if (customerId is { } id)
            await SubscriptionLocking.LockCustomerAsync(_db, id, cancellationToken);

        var subscription = await _db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, cancellationToken);
        if (subscription is { Status: SubscriptionPeriods.Active, EndDate: { } end } && end < today)
        {
            subscription.Status = SubscriptionPeriods.Expired;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Subscription {SubscriptionId} expired (paid through {EndDate}).", subscriptionId, end);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SendRemindersAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var leadUntil = today.AddDays(_options.ReminderLeadDays);
        var expiredSince = today.AddDays(-_options.ExpiredReminderMaxAgeDays);

        var due = await _db.CustomerSubscriptions.AsNoTracking()
            .Where(s => s.CustomerId != null && s.EndDate != null
                && s.Customer!.IsActive && s.Customer.DeletedAt == null)
            .Where(s => (s.Status == SubscriptionPeriods.Active && s.EndDate >= today && s.EndDate <= leadUntil)
                || (s.Status == SubscriptionPeriods.Expired && s.EndDate < today && s.EndDate >= expiredSince))
            .Select(s => new
            {
                s.SubscriptionId,
                CustomerId = s.CustomerId!.Value,
                s.Status,
                EndDate = s.EndDate!.Value,
                PlanName = s.Plan != null ? s.Plan.Name : "Premium",
            })
            .ToListAsync(cancellationToken);

        foreach (var s in due)
        {
            var kind = s.Status == SubscriptionPeriods.Expired ? ExpiredKind
                : s.EndDate == today ? EndDay
                : EndingSoon;

            try
            {
                await SendOnceAsync(s.SubscriptionId, s.CustomerId, kind, s.EndDate, s.PlanName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to send {Kind} reminder for subscription {SubscriptionId}.", kind, s.SubscriptionId);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task SendOnceAsync(
        Guid subscriptionId, Guid customerId, string kind, DateOnly periodEnd, string planName,
        CancellationToken cancellationToken)
    {
        var alreadySent = await _db.SubscriptionReminders.AsNoTracking().AnyAsync(r =>
            r.SubscriptionId == subscriptionId && r.Kind == kind && r.PeriodEndDate == periodEnd, cancellationToken);
        if (alreadySent)
            return;

        // The reminder claim and the notification row commit together, and the primary key on
        // subscription_reminders rejects a concurrent duplicate before any push goes out.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.SubscriptionReminders.Add(new SubscriptionReminder
        {
            SubscriptionId = subscriptionId,
            Kind = kind,
            PeriodEndDate = periodEnd,
            SentAt = DateTime.UtcNow,
        });

        var (title, message) = Compose(kind, planName, periodEnd);
        await _notifications.NotifyAsync(
            customerId, NotificationType, title, message, NotificationEntityType, subscriptionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Sent {Kind} reminder for subscription {SubscriptionId} (period ending {EndDate}).",
            kind, subscriptionId, periodEnd);
    }

    internal static (string Title, string Message) Compose(string kind, string planName, DateOnly periodEnd)
    {
        var date = periodEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return kind switch
        {
            EndingSoon => ($"Gói {planName} sắp hết hạn",
                $"Gói {planName} của bạn sẽ hết hạn vào ngày {date}. Gia hạn ngay để không bị gián đoạn các tính năng Premium."),
            EndDay => ($"Gói {planName} hết hạn hôm nay",
                $"Gói {planName} của bạn hết hạn vào cuối ngày hôm nay ({date}). Gia hạn để tiếp tục sử dụng các tính năng Premium."),
            _ => ($"Gói {planName} đã hết hạn",
                $"Gói {planName} của bạn đã hết hạn vào ngày {date}. Đăng ký lại để tiếp tục sử dụng các tính năng Premium."),
        };
    }
}
