namespace FinViet.Application.DTOs.Budgets;

public class UpsertBudgetRequest
{
    public Guid CategoryId { get; set; }

    public Guid? WalletId { get; set; }

    public decimal MonthlyLimit { get; set; }
}

public class UpdateBudgetRequest
{
    public decimal MonthlyLimit { get; set; }
}

public class BudgetResponse
{
    public Guid Id { get; set; }

    public Guid? CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public Guid? WalletId { get; set; }

    public decimal MonthlyLimit { get; set; }

    public decimal Spent { get; set; }

    public decimal Remaining { get; set; }

    public decimal Percentage { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal ExpectedSpent { get; set; }

    public decimal PaceDeviation { get; set; }

    public string PaceStatus { get; set; } = string.Empty;
}
