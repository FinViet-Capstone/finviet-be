using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Queries.GetSavingsPlanRecommendation;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Infrastructure.Features.Profile.Queries.GetSavingsPlanRecommendation;

public class GetSavingsPlanRecommendationQueryHandler
    : IRequestHandler<GetSavingsPlanRecommendationQuery, SavingsPlanRecommendationDto>
{
    private readonly IIncomeAllocationService _incomeAllocationService;

    public GetSavingsPlanRecommendationQueryHandler(IIncomeAllocationService incomeAllocationService)
        => _incomeAllocationService = incomeAllocationService;

    public Task<SavingsPlanRecommendationDto> Handle(
        GetSavingsPlanRecommendationQuery request, CancellationToken cancellationToken)
        => _incomeAllocationService.GetSavingsPlanRecommendationAsync(
            request.CustomerId, request.Month, cancellationToken);
}
