using FinViet.Application.DTOs.SubscriptionPlans;
using MediatR;

namespace FinViet.Application.Features.SubscriptionPlans.Commands.CreateSubscriptionPlan;

public record CreateSubscriptionPlanCommand(
    string Code,
    string Name,
    decimal Price,
    short BillingIntervalMonths,
    string[] Features
) : IRequest<SubscriptionPlanDto>;
