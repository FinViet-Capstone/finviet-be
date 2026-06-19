using MediatR;
using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Features.Transactions.Queries;
using FinViet.Application.Interfaces;

namespace FinViet.Application.Features.Transactions.Handlers;

public class GetTransactionsHandler
    : IRequestHandler<GetTransactionsQuery, PagedResult<TransactionResponseDto>>
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionsHandler(ITransactionRepository transactionRepository)
        => _transactionRepository = transactionRepository;

    public Task<PagedResult<TransactionResponseDto>> Handle(
        GetTransactionsQuery request, CancellationToken cancellationToken)
        => _transactionRepository.GetPagedAsync(request.CustomerId, request.Filter, cancellationToken);
}

public class GetTransactionByIdHandler
    : IRequestHandler<GetTransactionByIdQuery, TransactionResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionByIdHandler(ITransactionRepository transactionRepository)
        => _transactionRepository = transactionRepository;

    public async Task<TransactionResponseDto> Handle(
        GetTransactionByIdQuery request, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdForCustomerAsync(
            request.CustomerId, request.TransactionId, cancellationToken);

        // 404 for both "missing" and "not owned" so we don't leak existence of other customers' rows.
        if (transaction is null)
            throw new NotFoundException("Transaction", request.TransactionId);

        return transaction;
    }
}

public class GetTransactionSummaryHandler
    : IRequestHandler<GetTransactionSummaryQuery, TransactionSummaryResponseDto>
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionSummaryHandler(ITransactionRepository transactionRepository)
        => _transactionRepository = transactionRepository;

    public Task<TransactionSummaryResponseDto> Handle(
        GetTransactionSummaryQuery request, CancellationToken cancellationToken)
    {
        if (request.Month is < 1 or > 12)
            throw new BadRequestException("month must be between 1 and 12.");

        return _transactionRepository.GetSummaryAsync(
            request.CustomerId, request.Year, request.Month, cancellationToken);
    }
}
