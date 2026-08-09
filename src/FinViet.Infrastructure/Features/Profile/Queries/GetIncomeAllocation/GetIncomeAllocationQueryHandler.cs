using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Queries.GetIncomeAllocation;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Infrastructure.Features.Profile.Queries.GetIncomeAllocation;

public class GetIncomeAllocationQueryHandler
    : IRequestHandler<GetIncomeAllocationQuery, IncomeAllocationSummaryDto>
{
    private readonly IIncomeAllocationService _incomeAllocationService;

    public GetIncomeAllocationQueryHandler(IIncomeAllocationService incomeAllocationService)
        => _incomeAllocationService = incomeAllocationService;

    public Task<IncomeAllocationSummaryDto> Handle(
        GetIncomeAllocationQuery request, CancellationToken cancellationToken)
        => _incomeAllocationService.GetSummaryAsync(request.CustomerId, cancellationToken);
}
