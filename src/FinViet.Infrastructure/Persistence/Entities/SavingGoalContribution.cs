using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SavingGoalContribution
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid ContributionId { get; set; }

    public Guid? GoalId { get; set; }

    public Guid? TransactionId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Maps to column <c>contributed_at</c> in the v3 schema.</summary>
    public DateTime? ContributionDate { get; set; }

    public string? Note { get; set; }

    public virtual SavingGoal? Goal { get; set; }
}
