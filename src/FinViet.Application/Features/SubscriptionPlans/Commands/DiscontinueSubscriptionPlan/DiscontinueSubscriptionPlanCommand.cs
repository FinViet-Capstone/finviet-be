using FinViet.Application.DTOs.SubscriptionPlans;
using MediatR;

namespace FinViet.Application.Features.SubscriptionPlans.Commands.DiscontinueSubscriptionPlan;

/// <summary>
/// Sets IsActive = false only. Never cascades to CustomerSubscription — discontinuing only
/// blocks *new* subscribes (see SubscribeToPlanCommandHandler); existing auto-renewals keep
/// charging LockedPrice against this plan indefinitely.
/// </summary>
public record DiscontinueSubscriptionPlanCommand(Guid PlanId) : IRequest<SubscriptionPlanDto>;
