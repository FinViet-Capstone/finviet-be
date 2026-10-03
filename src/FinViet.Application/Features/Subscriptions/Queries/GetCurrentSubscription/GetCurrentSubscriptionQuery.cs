using MediatR;

namespace FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;

/// <param name="NextBillingDate">First day not covered by the paid period (ExpiresAt + 1).</param>
/// <param name="ExpiresAt">Last day the paid period covers (Vietnam calendar day, inclusive).</param>
public sealed record CurrentSubscriptionDto(Guid SubscriptionId, Guid? PlanId, string PlanName,
    string Status, decimal LockedPrice, DateOnly? NextBillingDate, DateOnly? ExpiresAt);

public sealed record GetCurrentSubscriptionQuery(Guid CustomerId) : IRequest<CurrentSubscriptionDto?>;
