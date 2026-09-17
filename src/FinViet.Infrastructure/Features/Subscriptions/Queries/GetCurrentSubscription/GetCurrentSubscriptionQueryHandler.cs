using FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Subscriptions.Queries.GetCurrentSubscription;

internal sealed class GetCurrentSubscriptionQueryHandler(FinVietDbContext db)
    : IRequestHandler<GetCurrentSubscriptionQuery, CurrentSubscriptionDto?>
{
    public Task<CurrentSubscriptionDto?> Handle(GetCurrentSubscriptionQuery request, CancellationToken cancellationToken) =>
        db.CustomerSubscriptions.AsNoTracking()
            .Where(s => s.CustomerId == request.CustomerId && (s.Status == "active" || s.Status == "past_due"))
            .OrderByDescending(s => s.Status == "active").ThenByDescending(s => s.CreatedAt)
            .Select(s => new CurrentSubscriptionDto(s.SubscriptionId, s.PlanId,
                s.Plan != null ? s.Plan.Name : "Premium", s.Status, s.LockedPrice, s.NextBillingDate))
            .FirstOrDefaultAsync(cancellationToken);
}
