using System.Linq.Expressions;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Services.Subscriptions;

/// <summary>
/// payOS charges are one-off QR payments, so each successful payment buys exactly one billing
/// interval. A subscription covers <see cref="CustomerSubscription.StartDate"/> through
/// <see cref="CustomerSubscription.EndDate"/> inclusive (Vietnam calendar days), and
/// <see cref="CustomerSubscription.NextBillingDate"/> is the first uncovered day (EndDate + 1).
/// </summary>
internal static class SubscriptionPeriods
{
    internal const string Active = "active";
    internal const string Expired = "expired";

    /// <summary>The single definition of "premium right now", shared by every reader so the
    /// customer's app, admin analytics and the admin user list can never disagree. The date check
    /// keeps a lapsed row from counting even before the lifecycle job flips it to expired.</summary>
    internal static Expression<Func<CustomerSubscription, bool>> ActiveOn(DateOnly today) =>
        s => s.Status == Active && (s.EndDate == null || s.EndDate >= today);

    /// <summary>Covers one billing interval starting on <paramref name="periodStart"/>.</summary>
    internal static void CoverInterval(CustomerSubscription subscription, DateOnly periodStart, int billingIntervalMonths)
    {
        var nextBillingDate = periodStart.AddMonths(billingIntervalMonths);
        subscription.NextBillingDate = nextBillingDate;
        subscription.EndDate = nextBillingDate.AddDays(-1);
    }
}
