using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CustomerSubscription
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid SubscriptionId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? PlanId { get; set; }

    /// <summary>Postgres enum <c>subscription_status</c> (active/canceled/expired/past_due).</summary>
    public string Status { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Snapshotted from <see cref="SubscriptionPlan.Price"/> the moment this subscription was
    /// created and never re-read afterward. Every renewal charge (see
    /// SubscriptionRenewalScheduler) charges this value, never the plan's live price — this is
    /// what makes editing SubscriptionPlan.Price in place safe for existing subscribers.
    /// </summary>
    public decimal LockedPrice { get; set; }

    public bool AutoRenew { get; set; }

    public DateOnly? NextBillingDate { get; set; }

    public DateOnly? NextRetryAt { get; set; }

    public int RetryCount { get; set; }

    /// <summary>Lease timestamp for the renewal job's claim/lease pattern. Stale after 15 minutes.</summary>
    public DateTime? RenewalClaimedAt { get; set; }

    public string? VnpayCardToken { get; set; }

    public DateTime? CanceledAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual SubscriptionPlan? Plan { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
