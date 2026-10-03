using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Records that one resubscribe reminder was sent for one paid period of a subscription. The key
/// (subscription, kind, period end) makes each reminder at-most-once per period, and a renewal
/// moves the end date so the next period's reminders are naturally re-armed.
/// </summary>
public partial class SubscriptionReminder
{
    public Guid SubscriptionId { get; set; }

    /// <summary>'ending_soon' | 'end_day' | 'expired'.</summary>
    public string Kind { get; set; } = null!;

    /// <summary>The <see cref="CustomerSubscription.EndDate"/> of the period this reminder was about.</summary>
    public DateOnly PeriodEndDate { get; set; }

    public DateTime SentAt { get; set; }

    public virtual CustomerSubscription Subscription { get; set; } = null!;
}
