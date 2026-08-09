using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Queries.GetIncomeAllocation;

public record GetIncomeAllocationQuery(Guid CustomerId) : IRequest<IncomeAllocationSummaryDto>;
