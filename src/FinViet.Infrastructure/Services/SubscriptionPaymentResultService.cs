using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Applies a payment outcome to a Payment row (and, on success, creates/renews the
/// CustomerSubscription). Used by the PayOS webhook handler. Callers are responsible for
/// loading and row-locking (FOR UPDATE) the Payment before calling this, within an open
/// transaction; this method re-checks terminal state so a duplicate call (e.g. a PayOS
/// webhook retry) is always a safe no-op.
/// </summary>
internal interface ISubscriptionPaymentResultService
{
    Task<bool> ApplyResultAsync(
        Payment payment,
        bool success,
        string? providerTransactionId,
        string? rawPayload,
        CancellationToken cancellationToken = default);
}

internal sealed class SubscriptionPaymentResultService : ISubscriptionPaymentResultService
{
    private const string Pending = "pending";
    private const string Succeeded = "succeeded";
    private const string Failed = "failed";
    private const string Active = "active";
    private const string Initial = "initial";

    private readonly FinVietDbContext _db;
    private readonly ILogger<SubscriptionPaymentResultService> _logger;

    public SubscriptionPaymentResultService(FinVietDbContext db, ILogger<SubscriptionPaymentResultService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> ApplyResultAsync(
        Payment payment,
        bool success,
        string? providerTransactionId,
        string? rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (payment.Status != Pending)
        {
            return false;
        }

        payment.PayosTransactionId = providerTransactionId;
        payment.RawWebhookPayload = rawPayload;
        payment.UpdatedAt = DateTime.UtcNow;

        if (!success)
        {
            payment.Status = Failed;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        payment.Status = Succeeded;
        payment.PaidAt = DateTime.UtcNow;

        var plan = await _db.SubscriptionPlans
            .FirstAsync(p => p.PlanId == payment.PlanId, cancellationToken);
        var todayVn = TodayVn();

        if (payment.ChargeType == Initial)
        {
            var subscription = new CustomerSubscription
            {
                SubscriptionId = Guid.NewGuid(),
                CustomerId = payment.CustomerId,
                PlanId = payment.PlanId,
                Status = Active,
                StartDate = todayVn,
                EndDate = null,
                LockedPrice = payment.Amount,
                AutoRenew = false,
                NextBillingDate = todayVn.AddMonths(plan.BillingIntervalMonths),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            try
            {
                _db.CustomerSubscriptions.Add(subscription);
                payment.SubscriptionId = subscription.SubscriptionId;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex,
                    "Payment {PaymentId} succeeded at PayOS but customer {CustomerId} already had an active subscription; marking failed.",
                    payment.PaymentId, payment.CustomerId);
                _db.Entry(subscription).State = EntityState.Detached;
                payment.SubscriptionId = null;
                payment.Status = Failed;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            var subscription = await _db.CustomerSubscriptions
                .FirstAsync(s => s.SubscriptionId == payment.SubscriptionId!.Value, cancellationToken);
            subscription.Status = Active;
            subscription.NextBillingDate = (subscription.NextBillingDate ?? todayVn).AddMonths(plan.BillingIntervalMonths);
            subscription.RetryCount = 0;
            subscription.NextRetryAt = null;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static DateOnly TodayVn()
    {
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveVietnamTimeZone());
        return DateOnly.FromDateTime(nowVn);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN+7", TimeSpan.FromHours(7), "Vietnam (+7)", "Vietnam (+7)");
    }
}
