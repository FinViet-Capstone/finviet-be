namespace FinViet.Application.DTOs;

public class CreateTransactionDto
{
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public string? EntryMethod { get; set; }
}

public class UpdateTransactionDto
{
    public string? CategoryId { get; set; }
}

public class ClassifyTransactionDto
{
    public string? CategoryId { get; set; }
}

public class TransactionResponseDto
{
    public Guid TransactionId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string SourceChannel { get; set; } = string.Empty;
    public string EntryMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public Guid? TransferPairId { get; set; }
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
