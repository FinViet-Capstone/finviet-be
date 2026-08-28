using FinViet.Application.DTOs;
using FinViet.Application.Features.Profile.Commands.ApplySavingsPlanRecommendation;
using FinViet.Application.Interfaces;
using MediatR;

namespace FinViet.Infrastructure.Features.Profile.Commands.ApplySavingsPlanRecommendation;

public class ApplySavingsPlanRecommendationCommandHandler
    : IRequestHandler<ApplySavingsPlanRecommendationCommand, IncomeAllocationEntryDto>
{
    private readonly IIncomeAllocationService _incomeAllocationService;

    public ApplySavingsPlanRecommendationCommandHandler(IIncomeAllocationService incomeAllocationService)
        => _incomeAllocationService = incomeAllocationService;

    public Task<IncomeAllocationEntryDto> Handle(
        ApplySavingsPlanRecommendationCommand request, CancellationToken cancellationToken)
        => _incomeAllocationService.ApplySavingsPlanRecommendationAsync(
            request.CustomerId, cancellationToken);
}
