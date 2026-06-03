using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SavingGoalContribution
{
    public Guid ContributionId { get; set; }

    public Guid? GoalId { get; set; }

    public decimal Amount { get; set; }

    public DateTime? ContributionDate { get; set; }

    public virtual SavingGoal? Goal { get; set; }
}
