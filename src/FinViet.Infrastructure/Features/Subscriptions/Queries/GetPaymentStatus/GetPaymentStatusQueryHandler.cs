using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Subscriptions.Queries.GetPaymentStatus;

internal sealed class GetPaymentStatusQueryHandler(
    FinVietDbContext db,
    IPaymentGateway gateway,
    ISubscriptionPaymentResultService resultService,
    ILogger<GetPaymentStatusQueryHandler> logger)
    : IRequestHandler<GetPaymentStatusQuery, SubscriptionPaymentStatusDto>
{
    private const string Pending = "pending";

    public async Task<SubscriptionPaymentStatusDto> Handle(GetPaymentStatusQuery request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderCode == request.OrderCode && p.CustomerId == request.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            throw new NotFoundException("Payment", request.OrderCode);
        }

        if (payment.Status == Pending)
        {
            var reconciled = await ReconcileWithGatewayAsync(payment, cancellationToken);
            if (reconciled is not null)
            {
                payment = reconciled;
            }
        }

        return new SubscriptionPaymentStatusDto(payment.OrderCode!.Value, payment.Status, payment.Amount, payment.SubscriptionId);
    }

    // Self-healing fallback: payOS only webhooks reliably on success, so an abandoned/expired
    // QR would otherwise stay "pending" forever. Poll payOS directly and apply the outcome
    // through the same ApplyResultAsync path the webhook uses, so idempotency / terminal-state
    // handling is shared rather than duplicated.
    private async Task<Payment?> ReconcileWithGatewayAsync(Payment payment, CancellationToken cancellationToken)
    {
        PaymentStatusResult remoteStatus;
        try
        {
            remoteStatus = await gateway.GetOrderStatusAsync(payment.OrderCode!.Value, cancellationToken);
        }
        catch (ExternalServiceException ex)
        {
            logger.LogWarning(ex,
                "PayOS status reconciliation failed for orderCode {OrderCode}; leaving payment pending",
                payment.OrderCode);
            return null;
        }

        if (remoteStatus.Status == PaymentGatewayStatus.Pending)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var locked = await PaymentLocking.LockByOrderCodeAsync(db, payment.OrderCode!.Value, cancellationToken);
        if (locked is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await resultService.ApplyResultAsync(
            locked,
            remoteStatus.Status == PaymentGatewayStatus.Succeeded,
            remoteStatus.Amount,
            remoteStatus.TransactionId,
            rawPayload: null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return locked;
    }
}
