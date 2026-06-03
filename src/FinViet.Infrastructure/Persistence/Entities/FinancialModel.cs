using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class FinancialModel
{
    public Guid ModelId { get; set; }

    public string ModelType { get; set; } = null!;

    public string? Description { get; set; }

    public string? AllocationRules { get; set; }

    public virtual ICollection<BudgetPlan> BudgetPlans { get; set; } = new List<BudgetPlan>();

    public virtual ICollection<ModelAllocation> ModelAllocations { get; set; } = new List<ModelAllocation>();
}
