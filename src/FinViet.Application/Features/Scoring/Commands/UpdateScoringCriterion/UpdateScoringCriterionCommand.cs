using FinViet.Application.DTOs.Scoring;
using MediatR;

namespace FinViet.Application.Features.Scoring.Commands.UpdateScoringCriterion;

public record UpdateScoringCriterionCommand(string Code, decimal WeightWeekly, decimal WeightMonthly)
    : IRequest<ScoringCriterionResponse>;
