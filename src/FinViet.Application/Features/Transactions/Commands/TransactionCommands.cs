using MediatR;
using FinViet.Application.DTOs;

namespace FinViet.Application.Features.Transactions.Commands;

public class CreateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    public string TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Note { get; set; }
}

public class UpdateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid TransactionId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SourceId { get; set; }
    public string TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Note { get; set; }
}

public class DeleteTransactionCommand : IRequest<bool>
{
    public Guid TransactionId { get; set; }
}
