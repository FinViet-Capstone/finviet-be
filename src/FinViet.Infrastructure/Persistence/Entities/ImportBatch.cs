using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class ImportBatch
{
    public Guid BatchId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? WalletId { get; set; }

    public string FileName { get; set; } = null!;

    public DateTime? ImportDate { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual Wallet? Wallet { get; set; }
}
