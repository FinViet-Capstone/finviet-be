using FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Subscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Subscriptions.Queries.GetCurrentSubscription;

internal sealed class GetCurrentSubscriptionQueryHandler(FinVietDbContext db, TimeProvider time)
    : IRequestHandler<GetCurrentSubscriptionQuery, CurrentSubscriptionDto?>
{
    public Task<CurrentSubscriptionDto?> Handle(GetCurrentSubscriptionQuery request, CancellationToken cancellationToken) =>
        db.CustomerSubscriptions.AsNoTracking()
            .Where(s => s.CustomerId == request.CustomerId)
            .Where(SubscriptionPeriods.ActiveOn(VietnamClock.Today(time)))
            .OrderByDescending(s => s.EndDate).ThenByDescending(s => s.CreatedAt)
            .Select(s => new CurrentSubscriptionDto(s.SubscriptionId, s.PlanId,
                s.Plan != null ? s.Plan.Name : "Premium", s.Status, s.LockedPrice, s.NextBillingDate, s.EndDate))
            .FirstOrDefaultAsync(cancellationToken);
}
