using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SavingGoal
{
    public Guid GoalId { get; set; }

    public Guid? CustomerId { get; set; }

    public string GoalName { get; set; } = null!;

    public decimal TargetAmount { get; set; }

    public decimal? CurrentAmount { get; set; }

    public DateOnly? Deadline { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<SavingGoalContribution> SavingGoalContributions { get; set; } = new List<SavingGoalContribution>();
}
