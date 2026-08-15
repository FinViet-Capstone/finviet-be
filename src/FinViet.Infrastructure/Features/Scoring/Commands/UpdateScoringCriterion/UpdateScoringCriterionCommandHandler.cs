using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Scoring;
using FinViet.Application.Features.Scoring.Commands.UpdateScoringCriterion;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Scoring.Commands.UpdateScoringCriterion;

public class UpdateScoringCriterionCommandHandler
    : IRequestHandler<UpdateScoringCriterionCommand, ScoringCriterionResponse>
{
    private readonly FinVietDbContext _db;
    public UpdateScoringCriterionCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<ScoringCriterionResponse> Handle(
        UpdateScoringCriterionCommand request, CancellationToken cancellationToken)
    {
        if (request.WeightWeekly is < 0 or > 100 || request.WeightMonthly is < 0 or > 100)
            throw new BadRequestException("Weights must be between 0 and 100.");

        var criterion = await _db.ScoringCriteria
            .FirstOrDefaultAsync(c => c.Code == request.Code, cancellationToken);

        if (criterion is null)
            throw new NotFoundException("ScoringCriterion", request.Code);

        criterion.WeightWeekly = request.WeightWeekly;
        criterion.WeightMonthly = request.WeightMonthly;
        criterion.Version += 1;
        criterion.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new ScoringCriterionResponse
        {
            Code = criterion.Code,
            CriterionName = criterion.CriterionName,
            WeightWeekly = criterion.WeightWeekly,
            WeightMonthly = criterion.WeightMonthly,
            Version = criterion.Version,
            UpdatedAt = criterion.UpdatedAt
        };
    }
}
