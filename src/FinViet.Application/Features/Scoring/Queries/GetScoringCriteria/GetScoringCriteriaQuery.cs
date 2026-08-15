using FinViet.Application.DTOs.Scoring;
using MediatR;

namespace FinViet.Application.Features.Scoring.Queries.GetScoringCriteria;

public record GetScoringCriteriaQuery : IRequest<IReadOnlyList<ScoringCriterionResponse>>;
