namespace FinViet.Application.DTOs.Transactions;

/// <summary>Filters + paging for GET /transactions (spec §4).</summary>
public class TransactionQuery
{
    public Guid? WalletId { get; set; }

    /// <summary>expense | income | transfer_out | transfer_in.</summary>
    public string? Type { get; set; }

    public string? CategoryId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Only transactions with no category (excludes transfers).</summary>
    public bool UncategorizedOnly { get; set; }

    /// <summary>Hide goal-funding transactions (category cat_savings_goal).</summary>
    public bool HideGoalContributions { get; set; }

    /// <summary>Free-text match on merchant/description.</summary>
    public string? Q { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
