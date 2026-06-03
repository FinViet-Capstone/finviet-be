using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class ModelAllocation
{
    public Guid AllocationId { get; set; }

    public Guid? ModelId { get; set; }

    public string CategoryType { get; set; } = null!;

    public decimal Percentage { get; set; }

    public virtual FinancialModel? Model { get; set; }
}
