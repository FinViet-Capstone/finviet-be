using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SubscriptionPlan
{
    public Guid PlanId { get; set; }

    public string Name { get; set; } = null!;

    public decimal Price { get; set; }

    public string? FeaturesJson { get; set; }

    public virtual ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = new List<CustomerSubscription>();
}
