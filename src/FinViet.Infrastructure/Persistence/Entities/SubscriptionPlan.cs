using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SubscriptionPlan
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid PlanId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public decimal Price { get; set; }

    public string? FeaturesJson { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = new List<CustomerSubscription>();
}
