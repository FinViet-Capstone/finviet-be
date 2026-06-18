namespace FinViet.Application.DTOs;

/// <summary>Filter + paging options for GET /api/transactions.</summary>
public class TransactionQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>Restrict to a single wallet (still scoped to the authenticated customer).</summary>
    public Guid? WalletId { get; set; }

    /// <summary>INCOME, EXPENSE, TRANSFER or DEBT_PAYMENT (case-insensitive).</summary>
    public string? Type { get; set; }

    public Guid? CategoryId { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Free-text search over Note and BeneficiaryName.</summary>
    public string? Q { get; set; }

    /// <summary>Only transactions with no category assigned.</summary>
    public bool UncategorizedOnly { get; set; }
}

/// <summary>Monthly aggregate for GET /api/transactions/summary.</summary>
public class TransactionSummaryResponseDto
{
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
    public List<CategorySummaryItemDto> ByCategory { get; set; } = new();
    public List<DaySummaryItemDto> ByDay { get; set; } = new();
    public List<BeneficiarySummaryItemDto> TopBeneficiaries { get; set; } = new();
}

public class CategorySummaryItemDto
{
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal Total { get; set; }
}

public class DaySummaryItemDto
{
    public DateOnly Date { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
}

public class BeneficiarySummaryItemDto
{
    public string Beneficiary { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
