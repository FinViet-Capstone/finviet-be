using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Queries.GetPaymentStatus;

public record GetPaymentStatusQuery(Guid CustomerId, long OrderCode) : IRequest<SubscriptionPaymentStatusDto>;
