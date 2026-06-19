using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class CategoryBudget
{
    public Guid CategoryBudgetId { get; set; }

    public Guid? PlanId { get; set; }

    public string? CategoryId { get; set; }

    public Guid? WalletId { get; set; }

    public decimal AmountLimit { get; set; }

    public decimal? CurrentSpent { get; set; }

    public decimal? ThresholdPct { get; set; }

    public string? ThresholdType { get; set; }

    public decimal LastAlertThreshold { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual BudgetPlan? Plan { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
