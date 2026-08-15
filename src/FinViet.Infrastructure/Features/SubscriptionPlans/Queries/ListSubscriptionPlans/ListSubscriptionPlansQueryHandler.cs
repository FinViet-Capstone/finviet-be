using FinViet.Application.DTOs.SubscriptionPlans;
using FinViet.Application.Features.SubscriptionPlans.Queries.ListSubscriptionPlans;
using FinViet.Infrastructure.Features.SubscriptionPlans;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.SubscriptionPlans.Queries.ListSubscriptionPlans;

internal class ListSubscriptionPlansQueryHandler : IRequestHandler<ListSubscriptionPlansQuery, List<SubscriptionPlanDto>>
{
    private readonly FinVietDbContext _db;

    public ListSubscriptionPlansQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<List<SubscriptionPlanDto>> Handle(ListSubscriptionPlansQuery request, CancellationToken cancellationToken)
    {
        var query = _db.SubscriptionPlans.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive)
            query = query.Where(p => p.IsActive);

        var plans = await query.OrderBy(p => p.Price).ToListAsync(cancellationToken);
        return plans.Select(SubscriptionPlanDtoMapper.ToDto).ToList();
    }
}
