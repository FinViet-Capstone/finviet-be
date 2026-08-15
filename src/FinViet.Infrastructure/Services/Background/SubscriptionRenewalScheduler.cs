using FinViet.Infrastructure.ExternalServices.VNPay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services.Background;

/// <summary>
/// Polls for auto-renewing subscriptions due for their next charge and renews them via VNPay,
/// always charging CustomerSubscription.LockedPrice — never SubscriptionPlan.Price — so an admin
/// editing a plan's live price can never reprice an existing subscriber. Uses a claim/lease
/// pattern (FOR UPDATE SKIP LOCKED, not a blocking lock) since the charge itself is a slow
/// external HTTP call and must not block other workers or interactive requests. Retry/dunning:
/// 1/3/7-day backoff, 4 total attempts (~11-day window), then auto-cancel.
/// </summary>
public class SubscriptionRenewalScheduler : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan LeaseStaleness = TimeSpan.FromMinutes(15);
    private static readonly int[] RetryScheduleDays = [1, 3, 7];
    private const int MaxRenewalAttempts = 4;
    private const int BatchSize = 200;

    private const string Active = "active";
    private const string PastDue = "past_due";
    private const string Canceled = "canceled";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionRenewalScheduler> _logger;
    private readonly TimeZoneInfo _vietnamTz;

    public SubscriptionRenewalScheduler(IServiceScopeFactory scopeFactory, ILogger<SubscriptionRenewalScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _vietnamTz = ResolveVietnamTimeZone();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionRenewalScheduler batch failed.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunBatchAsync(CancellationToken stoppingToken)
    {
        var todayVn = TodayVn();
        List<Guid> claimedIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();

            // Single atomic statement: SKIP LOCKED selection + claim (set renewal_claimed_at) in
            // one CTE, so the row lock is only held for the statement's own duration — never
            // across the slow VNPay HTTP call that follows for each claimed row.
            claimedIds = await db.Database
                .SqlQuery<Guid>($"""
                    WITH due AS (
                        SELECT id FROM customer_subscriptions
                        WHERE auto_renew = true
                          AND status IN ({Active}, {PastDue})
                          AND COALESCE(next_retry_at, next_billing_date) <= {todayVn}
                          AND (renewal_claimed_at IS NULL OR renewal_claimed_at < now() - {LeaseStaleness})
                        ORDER BY id
                        FOR UPDATE SKIP LOCKED
                        LIMIT {BatchSize}
                    )
                    UPDATE customer_subscriptions cs
                    SET renewal_claimed_at = now()
                    FROM due
                    WHERE cs.id = due.id
                    RETURNING cs.id
                    """)
                .ToListAsync(stoppingToken);
        }

        if (claimedIds.Count == 0) return;

        _logger.LogInformation("SubscriptionRenewalScheduler: claimed {Count} subscriptions due {Date}.", claimedIds.Count, todayVn);

        foreach (var subscriptionId in claimedIds)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessSubscriptionAsync(subscriptionId, todayVn, stoppingToken);
            }
            catch (Exception ex)
            {
                // Deliberately no compensating write here — the 15-minute lease staleness check
                // above means a subscription left claimed by a failed/crashed attempt becomes
                // eligible again automatically on a later poll, without risking a second failure
                // in this catch block.
                _logger.LogError(ex, "Failed renewing subscription {SubscriptionId}.", subscriptionId);
            }
        }
    }

    private async Task ProcessSubscriptionAsync(Guid subscriptionId, DateOnly todayVn, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
        var vnpay = scope.ServiceProvider.GetRequiredService<IVNPayClient>();
        var resultService = scope.ServiceProvider.GetRequiredService<ISubscriptionPaymentResultService>();

        var subscription = await db.CustomerSubscriptions
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId, stoppingToken);
        if (subscription is null) return;

        var txnRef = $"REN{Guid.NewGuid():N}"[..34];
        var now = DateTime.UtcNow;

        // Commit the pending Payment row before making the external call — never hold a DB
        // transaction open across a slow HTTP request. uq_payments_one_pending_per_subscription
        // is the double-charge backstop if a stale/overlapping worker somehow reaches here too.
        await using (var claimTransaction = await db.Database.BeginTransactionAsync(stoppingToken))
        {
            db.Payments.Add(new Payment
            {
                PaymentId = Guid.NewGuid(),
                SubscriptionId = subscription.SubscriptionId,
                CustomerId = subscription.CustomerId!.Value,
                PlanId = subscription.PlanId!.Value,
                Amount = subscription.LockedPrice, // never SubscriptionPlan.Price
                ChargeType = "renewal",
                Status = "pending",
                VnpTxnRef = txnRef,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(stoppingToken);
            await claimTransaction.CommitAsync(stoppingToken);
        }

        VNPayChargeResult chargeResult;
        try
        {
            chargeResult = await vnpay.ChargeByTokenAsync(
                subscription.VnpayCardToken ?? string.Empty,
                subscription.LockedPrice,
                txnRef,
                "FinViet Premium renewal",
                stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VNPay renewal charge threw for subscription {SubscriptionId}.", subscriptionId);
            chargeResult = new VNPayChargeResult(false, null, null, null, null, null, null, ex.Message);
        }

        await using var applyTransaction = await db.Database.BeginTransactionAsync(stoppingToken);

        var lockedPayment = await db.Payments
            .FromSqlInterpolated($"""
                SELECT id, subscription_id, customer_id, plan_id, amount, charge_type, status,
                       vnp_txn_ref, vnp_transaction_no, vnp_response_code, vnp_transaction_status,
                       vnp_bank_code, vnp_card_type, vnp_pay_date, paid_at, raw_ipn_payload,
                       idempotency_key, created_at, updated_at
                FROM payments WHERE vnp_txn_ref = {txnRef} FOR UPDATE
                """)
            .SingleAsync(stoppingToken);

        await resultService.ApplyResultAsync(
            lockedPayment,
            chargeResult.Success,
            chargeResult.ResponseCode,
            chargeResult.TransactionStatus,
            chargeResult.TransactionNo,
            chargeResult.BankCode,
            chargeResult.CardType,
            chargeResult.PayDate,
            stoppingToken);

        // Re-fetch: ApplyResultAsync's success path (renewal) updates the same row directly, but
        // reload defensively in case tracking state differs across the two transactions.
        var trackedSubscription = await db.CustomerSubscriptions
            .FirstAsync(s => s.SubscriptionId == subscriptionId, stoppingToken);

        if (!chargeResult.Success)
            ApplyDunningFailure(trackedSubscription, todayVn);

        // Release the lease regardless of outcome.
        trackedSubscription.RenewalClaimedAt = null;
        await db.SaveChangesAsync(stoppingToken);
        await applyTransaction.CommitAsync(stoppingToken);
    }

    internal static void ApplyDunningFailure(CustomerSubscription subscription, DateOnly todayVn)
    {
        subscription.RetryCount += 1;
        subscription.UpdatedAt = DateTime.UtcNow;

        if (subscription.RetryCount >= MaxRenewalAttempts)
        {
            subscription.Status = Canceled;
            subscription.AutoRenew = false;
            subscription.EndDate = todayVn;
            subscription.NextBillingDate = null;
            subscription.NextRetryAt = null;
            subscription.CanceledAt = DateTime.UtcNow;
            return;
        }

        // First failure stays "active" (grace period, no visible interruption); escalates to
        // "past_due" from the second failure onward.
        if (subscription.RetryCount >= 2)
            subscription.Status = PastDue;

        var scheduleIndex = Math.Min(subscription.RetryCount - 1, RetryScheduleDays.Length - 1);
        subscription.NextRetryAt = todayVn.AddDays(RetryScheduleDays[scheduleIndex]);
    }

    private DateOnly TodayVn()
    {
        var nowVn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _vietnamTz);
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
