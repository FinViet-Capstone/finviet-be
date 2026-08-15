using FinViet.Application.DTOs.SubscriptionPlans;
using MediatR;

namespace FinViet.Application.Features.SubscriptionPlans.Queries.ListSubscriptionPlans;

public record ListSubscriptionPlansQuery(bool IncludeInactive) : IRequest<List<SubscriptionPlanDto>>;
