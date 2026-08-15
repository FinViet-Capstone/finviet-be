using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Application.UnitTests;

// Covers the one property the whole VNPay feature exists for: an existing subscriber's
// LockedPrice is snapshotted once (at initial success) and never re-read from
// SubscriptionPlan.Price on renewal — plus the idempotency guarantee (a duplicate VNPay IPN, or
// the renewal job's own charge response racing a confirming IPN, must be a safe no-op).
public class SubscriptionPaymentResultServiceTests
{
    private static SubscriptionPlan NewPlan(decimal price = 49000m, short billingIntervalMonths = 1) => new()
    {
        PlanId = Guid.NewGuid(),
        Code = "premium_monthly",
        Name = "Premium Monthly",
        Price = price,
        BillingIntervalMonths = billingIntervalMonths,
        IsActive = true,
    };

    private static Payment NewPayment(Guid planId, decimal amount, string chargeType, Guid? subscriptionId = null) => new()
    {
        PaymentId = Guid.NewGuid(),
        SubscriptionId = subscriptionId,
        CustomerId = Guid.NewGuid(),
        PlanId = planId,
        Amount = amount,
        ChargeType = chargeType,
        Status = "pending",
        VnpTxnRef = $"TEST{Guid.NewGuid():N}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task InitialSuccess_CreatesSubscription_LockedPriceMatchesPaymentAmount()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new SubscriptionPaymentResultService(db, NullLogger<SubscriptionPaymentResultService>.Instance);

        // Admin's catalog price has already moved to 109000 by the time this payment resolves —
        // the subscription must still lock in what was actually charged (59000), not the current
        // catalog price. This is the exact scenario the whole feature was built to prevent.
        var plan = NewPlan(price: 109000m, billingIntervalMonths: 1);
        var payment = NewPayment(plan.PlanId, amount: 59000m, chargeType: "initial");
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var applied = await service.ApplyResultAsync(payment, success: true, "00", "00", "TXN1", "NCB", "ATM", "20260815120000");

        Assert.True(applied);
        Assert.Equal("succeeded", payment.Status);
        Assert.NotNull(payment.SubscriptionId);

        var subscription = await db.CustomerSubscriptions.SingleAsync(s => s.SubscriptionId == payment.SubscriptionId);
        Assert.Equal(59000m, subscription.LockedPrice); // not 109000 — the live catalog price
        Assert.Equal("active", subscription.Status);
        Assert.True(subscription.AutoRenew);
        Assert.Equal(0, subscription.RetryCount);
    }

    [Fact]
    public async Task InitialFailure_MarksPaymentFailed_NoSubscriptionCreated()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new SubscriptionPaymentResultService(db, NullLogger<SubscriptionPaymentResultService>.Instance);

        var plan = NewPlan();
        var payment = NewPayment(plan.PlanId, amount: 49000m, chargeType: "initial");
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var applied = await service.ApplyResultAsync(payment, success: false, "24", null, null, null, null, null);

        Assert.True(applied);
        Assert.Equal("failed", payment.Status);
        Assert.Null(payment.SubscriptionId);
        Assert.False(await db.CustomerSubscriptions.AnyAsync());
    }

    [Fact]
    public async Task RenewalSuccess_AdvancesFromPreviousNextBillingDate_NotFromToday_AndResetsRetryState()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new SubscriptionPaymentResultService(db, NullLogger<SubscriptionPaymentResultService>.Instance);

        var plan = NewPlan(billingIntervalMonths: 1);
        var originalNextBillingDate = new DateOnly(2026, 8, 1); // in the past relative to "now" —
        // a renewal that ran late (e.g. after a dunning retry) must still advance from this
        // original due date, not from "today", to avoid cumulative drift across retry cycles.
        var subscription = new CustomerSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            PlanId = plan.PlanId,
            Status = "past_due",
            StartDate = new DateOnly(2026, 6, 1),
            LockedPrice = 49000m,
            AutoRenew = true,
            NextBillingDate = originalNextBillingDate,
            RetryCount = 2,
            NextRetryAt = new DateOnly(2026, 8, 15),
        };
        var payment = NewPayment(plan.PlanId, amount: 49000m, chargeType: "renewal", subscription.SubscriptionId);
        db.SubscriptionPlans.Add(plan);
        db.CustomerSubscriptions.Add(subscription);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        await service.ApplyResultAsync(payment, success: true, "00", "00", "TXN2", "NCB", "ATM", "20260815120000");

        var reloaded = await db.CustomerSubscriptions.SingleAsync(s => s.SubscriptionId == subscription.SubscriptionId);
        Assert.Equal(originalNextBillingDate.AddMonths(1), reloaded.NextBillingDate);
        Assert.Equal("active", reloaded.Status);
        Assert.Equal(0, reloaded.RetryCount);
        Assert.Null(reloaded.NextRetryAt);
    }

    [Fact]
    public async Task AlreadyResolvedPayment_IsANoOp_IdempotentAgainstDuplicateIpn()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new SubscriptionPaymentResultService(db, NullLogger<SubscriptionPaymentResultService>.Instance);

        var plan = NewPlan();
        var payment = NewPayment(plan.PlanId, amount: 49000m, chargeType: "initial");
        payment.Status = "succeeded"; // already resolved by an earlier call
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var applied = await service.ApplyResultAsync(payment, success: true, "00", "00", "TXN3", null, null, null);

        Assert.False(applied);
        Assert.False(await db.CustomerSubscriptions.AnyAsync()); // no duplicate subscription created
    }
}
