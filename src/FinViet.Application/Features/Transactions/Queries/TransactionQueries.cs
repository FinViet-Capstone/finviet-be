using MediatR;
using FinViet.Application.Common;
using FinViet.Application.DTOs;

namespace FinViet.Application.Features.Transactions.Queries;

/// <summary>Paged, filtered list of the customer's transactions (ownership via wallet).</summary>
public class GetTransactionsQuery : IRequest<PagedResult<TransactionResponseDto>>
{
    public Guid CustomerId { get; set; }
    public TransactionQueryDto Filter { get; set; } = new();
}

/// <summary>Single transaction by id, scoped to the authenticated customer.</summary>
public class GetTransactionByIdQuery : IRequest<TransactionResponseDto>
{
    public Guid CustomerId { get; set; }
    public Guid TransactionId { get; set; }
}

/// <summary>Income/expense aggregates for a given month.</summary>
public class GetTransactionSummaryQuery : IRequest<TransactionSummaryResponseDto>
{
    public Guid CustomerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}
