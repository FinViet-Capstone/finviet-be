using MediatR;
using FinViet.Application.DTOs;

namespace FinViet.Application.Features.Transactions.Commands;

public class CreateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public string? EntryMethod { get; set; }
}

public class UpdateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
}

public class DeleteTransactionCommand : IRequest<bool>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
}

public class ClassifyTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public string? CategoryId { get; set; }
}
