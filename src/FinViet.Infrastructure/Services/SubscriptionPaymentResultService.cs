using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Applies a payment outcome to a Payment row and, on success, gives the customer one more
/// billing interval: a new subscription, an extension of the active one from its current end date
/// (so paying early loses no days), or a reactivation of their lapsed one. A payment payOS
/// confirmed as paid is therefore never stored as failed, even when the customer paid twice. Used by the PayOS webhook handler and the payment-status
/// reconciliation fallback. Callers are responsible for loading and row-locking (FOR UPDATE)
/// the Payment before calling this, within an open transaction; this method re-checks terminal
/// state so a duplicate call (e.g. a PayOS webhook retry) is always a safe no-op. A reported
/// <paramref name="amount"/> that doesn't match <see cref="Payment.Amount"/> is treated as a
/// failure regardless of <paramref name="success"/>, so the subscription is never activated on
/// a mismatched amount (defense-in-depth against a plan-price change or a mis-provisioned order).
/// </summary>
internal interface ISubscriptionPaymentResultService
{
    Task<bool> ApplyResultAsync(
        Payment payment,
        bool success,
        int amount,
        string? providerTransactionId,
        string? rawPayload,
        CancellationToken cancellationToken = default);
}

internal sealed class SubscriptionPaymentResultService : ISubscriptionPaymentResultService
{
    private const string Pending = "pending";
    private const string Succeeded = "succeeded";
    private const string Failed = "failed";
    private const string Initial = "initial";
    private const string Renewal = "renewal";

    private readonly FinVietDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionPaymentResultService> _logger;

    public SubscriptionPaymentResultService(
        FinVietDbContext db, TimeProvider time, ILogger<SubscriptionPaymentResultService> logger)
    {
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> ApplyResultAsync(
        Payment payment,
        bool success,
        int amount,
        string? providerTransactionId,
        string? rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (payment.Status != Pending)
        {
            return false;
        }

        if (success && amount != payment.Amount)
        {
            _logger.LogWarning(
                "Payment {PaymentId} amount mismatch: expected {ExpectedAmount}, provider reported {ReportedAmount}; marking failed.",
                payment.PaymentId, payment.Amount, amount);
            success = false;
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
        var today = VietnamClock.Today(_time);

        await SubscriptionLocking.LockCustomerAsync(_db, payment.CustomerId, cancellationToken);

        // uq_active_subscription allows at most one active row, so prefer it; otherwise reuse the
        // customer's most recent (expired/canceled) row instead of piling up a row per payment.
        var subscription = await _db.CustomerSubscriptions
            .Where(s => s.CustomerId == payment.CustomerId)
            .OrderByDescending(s => s.Status == SubscriptionPeriods.Active)
            .ThenByDescending(s => s.EndDate)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            subscription = new CustomerSubscription
            {
                SubscriptionId = Guid.NewGuid(),
                CustomerId = payment.CustomerId,
                StartDate = today,
                AutoRenew = false,
                CreatedAt = DateTime.UtcNow,
            };
            _db.CustomerSubscriptions.Add(subscription);
            SubscriptionPeriods.CoverInterval(subscription, today, plan.BillingIntervalMonths);
            payment.ChargeType = Initial;
        }
        else if (subscription.Status == SubscriptionPeriods.Active && subscription.EndDate is { } end && end >= today)
        {
            SubscriptionPeriods.CoverInterval(subscription, end.AddDays(1), plan.BillingIntervalMonths);
            payment.ChargeType = Renewal;
        }
        else
        {
            // Lapsed (or a legacy row with no end date): a new paid period starts today.
            subscription.StartDate = today;
            subscription.CanceledAt = null;
            SubscriptionPeriods.CoverInterval(subscription, today, plan.BillingIntervalMonths);
            payment.ChargeType = Renewal;
        }

        // The latest paid plan governs: its price is what this period was bought at.
        subscription.Status = SubscriptionPeriods.Active;
        subscription.PlanId = payment.PlanId;
        subscription.LockedPrice = payment.Amount;
        subscription.UpdatedAt = DateTime.UtcNow;
        payment.SubscriptionId = subscription.SubscriptionId;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {PaymentId} ({ChargeType}) applied to subscription {SubscriptionId}; paid through {EndDate}.",
            payment.PaymentId, payment.ChargeType, subscription.SubscriptionId, subscription.EndDate);
        return true;
    }
}
