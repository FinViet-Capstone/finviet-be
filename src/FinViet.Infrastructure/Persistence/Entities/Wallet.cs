using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Wallet
{
    public Guid WalletId { get; set; }

    public Guid? CustomerId { get; set; }

    public string WalletName { get; set; } = null!;

    public string WalletType { get; set; } = null!;

    public decimal? Balance { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<CategoryBudget> CategoryBudgets { get; set; } = new List<CategoryBudget>();

    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
