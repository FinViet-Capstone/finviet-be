using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Persistence.Idempotency;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Subscriptions.Commands.CreatePayment;

internal class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, CreatePaymentResultDto>
{
    private const string Operation = "subscription-create-payment";
    private const string Active = "active";

    private readonly FinVietDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<CreatePaymentCommandHandler> _logger;

    public CreatePaymentCommandHandler(
        FinVietDbContext db,
        IPaymentGateway gateway,
        ILogger<CreatePaymentCommandHandler> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<CreatePaymentResultDto> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PlanId == request.PlanId, cancellationToken)
            ?? throw new NotFoundException("SubscriptionPlan", request.PlanId);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (request.IdempotencyKey is not null)
        {
            var requestHash = IdempotencyStore.ComputeRequestHash(request);
            var claim = await IdempotencyStore.ClaimAsync(
                _db, request.CustomerId, Operation, request.IdempotencyKey, requestHash, cancellationToken);

            if (claim.IsReplay)
            {
                var replay = IdempotencyStore.ReadReplay<CreatePaymentResultDto>(claim);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
        }

        if (!plan.IsActive)
            throw new BusinessRuleException("This plan is no longer offered.", "plan_discontinued");

        var hasActiveSubscription = await _db.CustomerSubscriptions
            .AsNoTracking()
            .AnyAsync(s => s.CustomerId == request.CustomerId && s.Status == Active, cancellationToken);
        if (hasActiveSubscription)
            throw new BusinessRuleException("You already have an active subscription.", "already_subscribed");

        var orderCode = GenerateOrderCode();
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            PlanId = plan.PlanId,
            SubscriptionId = null,
            Amount = plan.Price,
            ChargeType = "initial",
            Status = "pending",
            OrderCode = orderCode,
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var expiresAt = new DateTimeOffset(now, TimeSpan.Zero).AddMinutes(15);
        var orderResult = await _gateway.CreateOrderAsync(
            orderCode: orderCode,
            amount: (int)plan.Price,
            description: $"FinViet Premium",
            expiry: expiresAt,
            returnUrl: "https://finviet.app/payment/return",
            cancelUrl: "https://finviet.app/payment/cancel",
            cancellationToken);

        var response = new CreatePaymentResultDto
        {
            OrderCode = orderCode,
            QrCode = orderResult.QrCode,
            Amount = payment.Amount,
            ExpiresAt = expiresAt,
        };

        if (request.IdempotencyKey is not null)
        {
            await IdempotencyStore.CompleteAsync(
                _db, request.CustomerId, Operation, request.IdempotencyKey, response, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Created payment {PaymentId} with orderCode {OrderCode} for customer {CustomerId}, plan {PlanId}",
            payment.PaymentId, orderCode, request.CustomerId, plan.PlanId);

        return response;
    }

    private static long GenerateOrderCode()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000_000L;
        var rnd = Random.Shared.NextInt64(0, 1000);
        return ts * 1000 + rnd;
    }
}
