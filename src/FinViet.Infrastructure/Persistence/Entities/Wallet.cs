using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Wallet
{
    public Guid WalletId { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>Maps to column <c>name</c> in the v3 schema.</summary>
    public string WalletName { get; set; } = null!;

    /// <summary>Maps to column <c>type</c> (basic / finverse_linked).</summary>
    public string WalletType { get; set; } = null!;

    public decimal? Balance { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual FinverseLink? FinverseLink { get; set; }

    public virtual ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
