using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.ApplySavingsPlanRecommendation;

/// <summary>
/// Carries no proposal: the recommendation is recomputed server-side and applied, so a stale
/// client-held split can never be written. Takes effect from next calendar month, like every
/// other allocation change.
/// </summary>
public record ApplySavingsPlanRecommendationCommand(Guid CustomerId)
    : IRequest<IncomeAllocationEntryDto>;
