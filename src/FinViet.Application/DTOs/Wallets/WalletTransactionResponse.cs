namespace FinViet.Application.DTOs.Wallets;

public class WalletTransactionResponse
{
    public Guid TransactionId { get; set; }

    public Guid WalletId { get; set; }

    public string? CategoryId { get; set; }

    public string TransactionType { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTimeOffset TransactionDate { get; set; }

    public string? Note { get; set; }

    public string CategorizationStatus { get; set; } = "none";

    public string? AiSuggestedCategoryId { get; set; }

    public string? AiSuggestedCategoryName { get; set; }

    public decimal? AiConfidence { get; set; }

    public string? AiSource { get; set; }
}
