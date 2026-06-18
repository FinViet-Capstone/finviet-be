using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Category
{
    public Guid CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? NameVi { get; set; }

    public string? NameEn { get; set; }

    public string Type { get; set; } = null!;

    public bool? IsMandatory { get; set; }

    public string? ExpenseClass { get; set; }

    public string? Icon { get; set; }

    public string? Color { get; set; }

    public int? SortOrder { get; set; }

    public virtual ICollection<CategoryBudget> CategoryBudgets { get; set; } = new List<CategoryBudget>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
