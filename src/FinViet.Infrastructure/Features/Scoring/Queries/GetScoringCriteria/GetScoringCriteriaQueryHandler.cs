using FinViet.Application.DTOs.Scoring;
using FinViet.Application.Features.Scoring.Queries.GetScoringCriteria;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Scoring.Queries.GetScoringCriteria;

public class GetScoringCriteriaQueryHandler
    : IRequestHandler<GetScoringCriteriaQuery, IReadOnlyList<ScoringCriterionResponse>>
{
    private readonly FinVietDbContext _db;
    public GetScoringCriteriaQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<ScoringCriterionResponse>> Handle(
        GetScoringCriteriaQuery request, CancellationToken cancellationToken)
    {
        return await _db.ScoringCriteria
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new ScoringCriterionResponse
            {
                Code = c.Code,
                CriterionName = c.CriterionName,
                WeightWeekly = c.WeightWeekly,
                WeightMonthly = c.WeightMonthly,
                Version = c.Version,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
