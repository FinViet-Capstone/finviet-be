using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SavingGoal
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid GoalId { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>Maps to column <c>name</c> in the v3 schema.</summary>
    public string GoalName { get; set; } = null!;

    public string? IconEmoji { get; set; }

    public decimal TargetAmount { get; set; }

    public decimal? CurrentAmount { get; set; }

    public DateOnly? Deadline { get; set; }

    public Guid? FundingWalletId { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<SavingGoalContribution> SavingGoalContributions { get; set; } = new List<SavingGoalContribution>();
}
