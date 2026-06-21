using System.Collections.Generic;
using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Category
{
    public string CategoryId { get; set; } = null!;

    public string CategoryName { get; set; } = null!;

    public string? NameVi { get; set; }

    public string? NameEn { get; set; }

    public CategoryType Type { get; set; }

    public bool? IsMandatory { get; set; }

    public string? ExpenseClass { get; set; }

    public string? Icon { get; set; }

    public string? Color { get; set; }

    public int? SortOrder { get; set; }

    public virtual ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<CustomerCategory> CustomerCategories { get; set; } = new List<CustomerCategory>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
