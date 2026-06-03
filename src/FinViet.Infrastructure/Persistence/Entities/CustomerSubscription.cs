using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CustomerSubscription
{
    public Guid SubscriptionId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? PlanId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual SubscriptionPlan? Plan { get; set; }
}
