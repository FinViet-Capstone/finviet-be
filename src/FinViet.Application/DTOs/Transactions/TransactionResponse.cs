namespace FinViet.Application.DTOs.Transactions;

/// <summary>Full transaction representation returned by the transaction APIs.</summary>
public class TransactionResponse
{
    public Guid TransactionId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? EntryMethod { get; set; }
    public Guid? TransferPairId { get; set; }
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
