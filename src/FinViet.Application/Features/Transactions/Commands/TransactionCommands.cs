using MediatR;
using FinViet.Application.DTOs;

namespace FinViet.Application.Features.Transactions.Commands;

public class CreateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Note { get; set; }
    public string? Merchant { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? EntryMethod { get; set; }
    public string? AiSource { get; set; }
    public decimal? AiConfidence { get; set; }
}

public class UpdateTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public string? CategoryId { get; set; }
    public decimal? Amount { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionDate { get; set; }
}

public class DeleteTransactionCommand : IRequest<bool>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
}

/// <summary>
/// Splits one transaction across several categories. The original row is replaced by the
/// parts, whose amounts must sum to it exactly — so the wallet balance never moves and every
/// aggregation that groups by category keeps working unchanged.
/// </summary>
public class SplitTransactionCommand : IRequest<IReadOnlyList<TransactionResponseDto>>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public IReadOnlyList<SplitPartRequest> Parts { get; set; } = new List<SplitPartRequest>();
    public string? IdempotencyKey { get; set; }
}

public class ClassifyTransactionCommand : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
    public string? CategoryId { get; set; }
}
