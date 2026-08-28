using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Queries.GetSavingsPlanRecommendation;

/// <summary>
/// <paramref name="Month"/> (optional, <c>yyyy-MM</c>) measures the goals against that month's
/// effective split instead of the current month's. Omit for today.
/// </summary>
public record GetSavingsPlanRecommendationQuery(Guid CustomerId, string? Month = null)
    : IRequest<SavingsPlanRecommendationDto>;
