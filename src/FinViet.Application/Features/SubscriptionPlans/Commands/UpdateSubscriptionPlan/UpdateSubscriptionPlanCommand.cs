using FinViet.Application.DTOs.SubscriptionPlans;
using MediatR;

namespace FinViet.Application.Features.SubscriptionPlans.Commands.UpdateSubscriptionPlan;

/// <summary>
/// Deliberately excludes Code (immutable identifier). Updates Price in place — existing
/// subscribers are protected via CustomerSubscription.LockedPrice (snapshotted at subscribe
/// time and never re-read), so editing the live catalog price here never touches what an
/// existing subscriber is charged; it only affects new subscribes going forward.
/// </summary>
public record UpdateSubscriptionPlanCommand(
    Guid PlanId,
    string Name,
    decimal Price,
    short BillingIntervalMonths,
    string[] Features
) : IRequest<SubscriptionPlanDto>;
