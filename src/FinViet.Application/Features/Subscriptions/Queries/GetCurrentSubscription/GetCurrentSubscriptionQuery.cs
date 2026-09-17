using MediatR;

namespace FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;

public sealed record CurrentSubscriptionDto(Guid SubscriptionId, Guid? PlanId, string PlanName,
    string Status, decimal LockedPrice, DateOnly? NextBillingDate);

public sealed record GetCurrentSubscriptionQuery(Guid CustomerId) : IRequest<CurrentSubscriptionDto?>;
