using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Commands.CreatePayment;

public record CreatePaymentCommand(
    Guid CustomerId,
    Guid PlanId,
    string? IdempotencyKey
) : IRequest<CreatePaymentResultDto>;
