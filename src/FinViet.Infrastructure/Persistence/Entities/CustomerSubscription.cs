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

    public virtual Customer? Customer { get; set; }

    public virtual SubscriptionPlan? Plan { get; set; }
}
