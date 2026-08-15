using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services.Background;

namespace FinViet.Application.UnitTests;

// Covers SubscriptionRenewalScheduler.ApplyDunningFailure's retry/cancel state machine: 1/3/7-day
// backoff, escalation to past_due from the 2nd failure, auto-cancel on the 4th (~11-day window).
public class SubscriptionRenewalDunningTests
{
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static CustomerSubscription NewSubscription() => new()
    {
        SubscriptionId = Guid.NewGuid(),
        Status = "active",
        AutoRenew = true,
        RetryCount = 0,
        LockedPrice = 49000m,
    };

    [Fact]
    public void FirstFailure_StaysActive_GracePeriod_RetriesInOneDay()
    {
        var subscription = NewSubscription();

        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today);

        Assert.Equal(1, subscription.RetryCount);
        Assert.Equal("active", subscription.Status);
        Assert.Equal(Today.AddDays(1), subscription.NextRetryAt);
        Assert.True(subscription.AutoRenew);
    }

    [Fact]
    public void SecondFailure_EscalatesToPastDue_RetriesInThreeDays()
    {
        var subscription = NewSubscription();
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today);

        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today.AddDays(1));

        Assert.Equal(2, subscription.RetryCount);
        Assert.Equal("past_due", subscription.Status);
        Assert.Equal(Today.AddDays(1).AddDays(3), subscription.NextRetryAt);
    }

    [Fact]
    public void ThirdFailure_StaysPastDue_RetriesInSevenDays()
    {
        var subscription = NewSubscription();
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today);
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today.AddDays(1));

        var thirdAttemptDate = Today.AddDays(4);
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, thirdAttemptDate);

        Assert.Equal(3, subscription.RetryCount);
        Assert.Equal("past_due", subscription.Status);
        Assert.Equal(thirdAttemptDate.AddDays(7), subscription.NextRetryAt);
    }

    [Fact]
    public void FourthFailure_Cancels_StopsAutoRenew_ClearsBillingState()
    {
        var subscription = NewSubscription();
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today);
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today.AddDays(1));
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today.AddDays(4));

        var fourthAttemptDate = Today.AddDays(11);
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, fourthAttemptDate);

        Assert.Equal(4, subscription.RetryCount);
        Assert.Equal("canceled", subscription.Status);
        Assert.False(subscription.AutoRenew);
        Assert.Equal(fourthAttemptDate, subscription.EndDate);
        Assert.Null(subscription.NextBillingDate);
        Assert.Null(subscription.NextRetryAt);
        Assert.NotNull(subscription.CanceledAt);
    }

    [Fact]
    public void SuccessAfterFailures_IsHandledElsewhere_DunningOnlyAppliesOnFailure()
    {
        // ApplyDunningFailure is only ever called on the failure path (see
        // SubscriptionRenewalScheduler.ProcessSubscriptionAsync) — success resets RetryCount via
        // SubscriptionPaymentResultService.ApplyResultAsync instead, covered separately in
        // SubscriptionPaymentResultServiceTests. This test just documents the boundary.
        var subscription = NewSubscription();
        SubscriptionRenewalScheduler.ApplyDunningFailure(subscription, Today);
        Assert.Equal(1, subscription.RetryCount);
    }
}
