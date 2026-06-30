using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Budget
{
    public Guid BudgetId { get; set; }

    public Guid CustomerId { get; set; }

    public string CategoryId { get; set; } = null!;

    public Guid? WalletId { get; set; }

    public decimal MonthlyLimit { get; set; }

    public decimal LastAlertThreshold { get; set; }

    public string? LastAlertMonth { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
