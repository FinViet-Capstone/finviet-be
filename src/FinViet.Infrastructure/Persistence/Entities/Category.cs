using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Category
{
    public Guid CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string Type { get; set; } = null!;

    public bool? IsMandatory { get; set; }

    public string? ExpenseClass { get; set; }

    public virtual ICollection<CategoryBudget> CategoryBudgets { get; set; } = new List<CategoryBudget>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
