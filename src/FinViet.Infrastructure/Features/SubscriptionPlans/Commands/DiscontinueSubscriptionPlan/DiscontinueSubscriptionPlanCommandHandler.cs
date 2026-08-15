using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.SubscriptionPlans;
using FinViet.Application.Features.SubscriptionPlans.Commands.DiscontinueSubscriptionPlan;
using FinViet.Infrastructure.Features.SubscriptionPlans;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.SubscriptionPlans.Commands.DiscontinueSubscriptionPlan;

internal class DiscontinueSubscriptionPlanCommandHandler : IRequestHandler<DiscontinueSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly FinVietDbContext _db;

    public DiscontinueSubscriptionPlanCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<SubscriptionPlanDto> Handle(DiscontinueSubscriptionPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.PlanId == request.PlanId, cancellationToken)
            ?? throw new NotFoundException("SubscriptionPlan", request.PlanId);

        // Never cascades to CustomerSubscription — existing auto-renewals keep charging
        // LockedPrice against this plan indefinitely; only new subscribes are blocked.
        plan.IsActive = false;

        await _db.SaveChangesAsync(cancellationToken);

        return SubscriptionPlanDtoMapper.ToDto(plan);
    }
}
