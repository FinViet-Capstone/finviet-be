using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Single shared place that applies a VNPay charge outcome to a Payment (and, on success,
/// creates/renews the CustomerSubscription) — used by both ProcessVNPayIpnCommandHandler and
/// SubscriptionRenewalScheduler so the two call sites can never drift apart on this logic.
/// Callers are responsible for loading and row-locking (FOR UPDATE) the Payment before calling
/// this, within an open transaction; this method itself re-checks terminal state so a duplicate
/// call (e.g. a VNPay IPN retry) is always a safe no-op.
/// </summary>
internal interface ISubscriptionPaymentResultService
{
    Task<bool> ApplyResultAsync(
        Payment payment,
        bool success,
        string? responseCode,
        string? transactionStatus,
        string? transactionNo,
        string? bankCode,
        string? cardType,
        string? payDate,
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
        string? responseCode,
        string? transactionStatus,
        string? transactionNo,
        string? bankCode,
        string? cardType,
        string? payDate,
        CancellationToken cancellationToken = default)
    {
        if (payment.Status != Pending)
        {
            // Already resolved by an earlier call (VNPay IPN retry, or the renewal job's direct
            // charge response racing its own confirming IPN) — safe no-op, this is the
            // idempotency guarantee.
            return false;
        }

        payment.VnpResponseCode = responseCode;
        payment.VnpTransactionStatus = transactionStatus;
        payment.VnpTransactionNo = transactionNo;
        payment.VnpBankCode = bankCode;
        payment.VnpCardType = cardType;
        payment.VnpPayDate = payDate;
        payment.UpdatedAt = DateTime.UtcNow;

        if (!success)
        {
            payment.Status = Failed;
            await _db.SaveChangesAsync(cancellationToken);
            // Renewal dunning-state transitions (retry_count/next_retry_at/status) are owned
            // exclusively by SubscriptionRenewalScheduler — this method only records what
            // happened to this one payment attempt.
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
                // Snapshotted once, here, and never re-read from SubscriptionPlan.Price again —
                // this is the guarantee that makes editing SubscriptionPlan.Price in place safe.
                LockedPrice = payment.Amount,
                AutoRenew = true,
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
                // Lost a race against uq_active_subscription — two tabs both completed a VNPay
                // charge for the same customer. The charge succeeded on VNPay's side; a manual
                // refund is an ops follow-up, out of scope here. Record this payment as failed
                // rather than leaving two active subscriptions or crashing the IPN handler.
                _logger.LogWarning(ex,
                    "Payment {PaymentId} succeeded at VNPay but customer {CustomerId} already had an active subscription; marking failed.",
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
            // Advance from the previous NextBillingDate, not from "today" — prevents cumulative
            // drift across repeated dunning cycles.
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
