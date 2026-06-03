using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class IncomeSource
{
    public Guid SourceId { get; set; }

    public Guid? CustomerId { get; set; }

    public string Name { get; set; } = null!;

    public string? Type { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
