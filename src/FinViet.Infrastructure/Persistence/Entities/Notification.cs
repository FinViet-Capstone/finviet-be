using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Notification
{
    public Guid NotificationId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? CategoryBudgetId { get; set; }

    public Guid? GoalId { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public bool? IsRead { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual CategoryBudget? CategoryBudget { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual SavingGoal? Goal { get; set; }
}
