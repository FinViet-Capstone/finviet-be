using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Queries.GetSubscriptionPayment;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Subscriptions.Queries.GetSubscriptionPayment;

internal sealed class GetSubscriptionPaymentQueryHandler(FinVietDbContext db)
    : IRequestHandler<GetSubscriptionPaymentQuery, SubscriptionPaymentDto>
{
    public async Task<SubscriptionPaymentDto> Handle(GetSubscriptionPaymentQuery request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.PaymentId == request.PaymentId && p.CustomerId == request.CustomerId)
            .Select(p => new SubscriptionPaymentDto(p.PaymentId, p.Status, p.Amount, p.SubscriptionId))
            .SingleOrDefaultAsync(cancellationToken);
        return payment ?? throw new NotFoundException("Payment", request.PaymentId);
    }
}
