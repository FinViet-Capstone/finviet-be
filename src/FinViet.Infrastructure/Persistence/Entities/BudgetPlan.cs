using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class BudgetPlan
{
    public Guid PlanId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? ModelId { get; set; }

    public string PlanName { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal NeedsPct { get; set; } = 50m;

    public decimal WantsPct { get; set; } = 30m;

    public decimal SavingsPct { get; set; } = 20m;

    public virtual ICollection<CategoryBudget> CategoryBudgets { get; set; } = new List<CategoryBudget>();

    public virtual Customer? Customer { get; set; }

    public virtual FinancialModel? Model { get; set; }
}
