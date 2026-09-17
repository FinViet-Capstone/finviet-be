using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Queries.GetSubscriptionPayment;

public record GetSubscriptionPaymentQuery(Guid CustomerId, Guid PaymentId) : IRequest<SubscriptionPaymentDto>;
