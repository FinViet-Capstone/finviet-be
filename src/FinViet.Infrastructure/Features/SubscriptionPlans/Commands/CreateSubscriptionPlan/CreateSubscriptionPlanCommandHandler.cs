using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.SubscriptionPlans;
using FinViet.Application.Features.SubscriptionPlans.Commands.CreateSubscriptionPlan;
using FinViet.Infrastructure.Features.SubscriptionPlans;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.SubscriptionPlans.Commands.CreateSubscriptionPlan;

internal class CreateSubscriptionPlanCommandHandler : IRequestHandler<CreateSubscriptionPlanCommand, SubscriptionPlanDto>
{
    private readonly FinVietDbContext _db;

    public CreateSubscriptionPlanCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<SubscriptionPlanDto> Handle(CreateSubscriptionPlanCommand request, CancellationToken cancellationToken)
    {
        var codeTaken = await _db.SubscriptionPlans
            .AsNoTracking()
            .AnyAsync(p => p.Code == request.Code, cancellationToken);
        if (codeTaken)
            throw new ConflictException($"A plan with code '{request.Code}' already exists.");

        var plan = new SubscriptionPlan
        {
            PlanId = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Price = request.Price,
            BillingIntervalMonths = request.BillingIntervalMonths,
            FeaturesJson = SubscriptionPlanDtoMapper.SerializeFeatures(request.Features),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        _db.SubscriptionPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);

        return SubscriptionPlanDtoMapper.ToDto(plan);
    }
}
