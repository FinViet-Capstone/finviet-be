using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.SubscriptionPlans;
using FinViet.Application.Features.SubscriptionPlans.Commands.UpdateSubscriptionPlan;
using FinViet.Infrastructure.Features.SubscriptionPlans;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.SubscriptionPlans.Commands.UpdateSubscriptionPlan;

internal class UpdateSubscriptionPlanCommandHandler : IRequestHandler<UpdateSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly FinVietDbContext _db;

    public UpdateSubscriptionPlanCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<SubscriptionPlanDto> Handle(UpdateSubscriptionPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.PlanId == request.PlanId, cancellationToken)
            ?? throw new NotFoundException("SubscriptionPlan", request.PlanId);

        plan.Name = request.Name;
        // Existing subscribers are protected via CustomerSubscription.LockedPrice — editing
        // Price here never touches an existing subscriber's charge.
        plan.Price = request.Price;
        plan.BillingIntervalMonths = request.BillingIntervalMonths;
        plan.FeaturesJson = SubscriptionPlanDtoMapper.SerializeFeatures(request.Features);

        await _db.SaveChangesAsync(cancellationToken);

        return SubscriptionPlanDtoMapper.ToDto(plan);
    }
}
