namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>subscription_status</c> (active, canceled, expired, past_due).</summary>
public enum SubscriptionStatus
{
    Active,
    Canceled,
    Expired,
    PastDue
}
