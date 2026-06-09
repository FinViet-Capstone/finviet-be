namespace FinViet.Application.DTOs;

public class CreateTransactionDto
{
    public Guid WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
}

public class UpdateTransactionDto
{
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
}

public class ClassifyTransactionDto
{
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
}

public class TransactionResponseDto
{
    public Guid TransactionId { get; set; }
    public Guid WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
