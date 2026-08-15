using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Commands.SubscribeToPlan;

public record SubscribeToPlanCommand(
    Guid CustomerId,
    Guid PlanId,
    string ReturnUrl,
    string IpAddress,
    string? IdempotencyKey
) : IRequest<SubscribeToPlanResultDto>;
