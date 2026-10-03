namespace FinViet.Infrastructure.Services.Subscriptions;

/// <summary>Binds the "Subscriptions" section: when the lifecycle job runs and when it reminds
/// customers to resubscribe (finviet-be#138).</summary>
public sealed class SubscriptionLifecycleOptions
{
    public const string SectionName = "Subscriptions";

    /// <summary>Send the "ending soon" reminder this many days before the paid period ends.</summary>
    public int ReminderLeadDays { get; set; } = 3;

    /// <summary>How often the job runs. It is idempotent, so running often only shortens the gap
    /// between a period ending and the subscription being marked expired.</summary>
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>Reminders are only sent between these Vietnam local hours (start inclusive, end
    /// exclusive), so nobody gets a push in the middle of the night; expiry runs at any hour.</summary>
    public int ReminderStartHour { get; set; } = 8;

    public int ReminderEndHour { get; set; } = 21;

    /// <summary>An "expired" reminder is still sent if the job first sees the lapse up to this
    /// many days late (e.g. after downtime); older lapses are left alone.</summary>
    public int ExpiredReminderMaxAgeDays { get; set; } = 30;
}
