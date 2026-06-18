using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class BeneficiaryRule
{
    public Guid RuleId { get; set; }

    public Guid CustomerId { get; set; }

    public string MatchText { get; set; } = null!;

    public Guid CategoryId { get; set; }

    public bool IsRecurring { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Category? Category { get; set; }
}
