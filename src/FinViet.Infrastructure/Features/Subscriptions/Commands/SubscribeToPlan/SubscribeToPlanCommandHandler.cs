using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Commands.SubscribeToPlan;
using FinViet.Infrastructure.ExternalServices.VNPay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Idempotency;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Subscriptions.Commands.SubscribeToPlan;

internal class SubscribeToPlanCommandHandler : IRequestHandler<SubscribeToPlanCommand, SubscribeToPlanResultDto>
{
    private const string Operation = "subscription-subscribe";
    private const string Active = "active";

    private readonly FinVietDbContext _db;
    private readonly IVNPayClient _vnpay;

    public SubscribeToPlanCommandHandler(FinVietDbContext db, IVNPayClient vnpay)
    {
        _db = db;
        _vnpay = vnpay;
    }

    public async Task<SubscribeToPlanResultDto> Handle(SubscribeToPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlanId == request.PlanId, cancellationToken)
            ?? throw new NotFoundException("SubscriptionPlan", request.PlanId);

        if (!plan.IsActive)
            throw new BusinessRuleException("This plan is no longer offered.", "plan_discontinued");

        var hasActiveSubscription = await _db.CustomerSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.CustomerId == request.CustomerId && s.Status == Active, cancellationToken);
        if (hasActiveSubscription)
            throw new BusinessRuleException("You already have an active subscription.", "already_subscribed");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var requestHash = IdempotencyStore.ComputeRequestHash(request);
        var claim = await IdempotencyStore.ClaimAsync(
            _db, request.CustomerId, Operation, request.IdempotencyKey, requestHash, cancellationToken);

        if (claim.IsReplay)
        {
            var replay = IdempotencyStore.ReadReplay<SubscribeToPlanResultDto>(claim);
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var txnRef = $"SUB{Guid.NewGuid():N}"[..34];
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            PlanId = plan.PlanId,
            SubscriptionId = null, // no subscription exists yet — created once the IPN confirms success
            Amount = plan.Price, // this snapshot becomes the subscription's LockedPrice once activated
            ChargeType = "initial",
            Status = "pending",
            VnpTxnRef = txnRef,
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var redirectUrl = _vnpay.BuildPaymentUrl(new VNPayPaymentRequest(
            AmountVnd: plan.Price,
            TxnRef: txnRef,
            OrderInfo: $"FinViet Premium - {plan.Name}",
            IpAddress: request.IpAddress,
            ReturnUrlOverride: request.ReturnUrl));

        var response = new SubscribeToPlanResultDto { RedirectUrl = redirectUrl };

        await IdempotencyStore.CompleteAsync(
            _db, request.CustomerId, Operation, request.IdempotencyKey!, response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }
}
