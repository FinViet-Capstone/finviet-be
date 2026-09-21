using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Audit record for every PayOS charge attempt (initial subscribe or manual renewal).
/// Independent from personal-finance transactions and never changes a wallet balance.
/// </summary>
public partial class Payment
{
    public Guid PaymentId { get; set; }

    /// <summary>
    /// Null until the webhook handler confirms an "initial" charge and creates the
    /// CustomerSubscription row - a subscription can't exist before its first payment succeeds.
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid PlanId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>"initial" or "renewal".</summary>
    public string ChargeType { get; set; } = null!;

    /// <summary>Postgres enum <c>payment_status</c> (pending/succeeded/failed/canceled).</summary>
    public string Status { get; set; } = null!;

    /// <summary>PayOS's numeric order identifier, unique per merchant.</summary>
    public long? OrderCode { get; set; }

    /// <summary>PayOS's own transaction reference from the webhook confirmation.</summary>
    public string? PayosTransactionId { get; set; }

    public DateTime? PaidAt { get; set; }

    /// <summary>Full webhook JSON payload for audit.</summary>
    public string? RawWebhookPayload { get; set; }

    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CustomerSubscription? Subscription { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual SubscriptionPlan Plan { get; set; } = null!;
}
