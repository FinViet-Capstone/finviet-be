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
    private const int MaxOrderCodeAttempts = 5;

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

    /// <summary>Test seam: lets a unit test force an order-code collision deterministically.</summary>
    internal Func<long> OrderCodeFactory { get; set; } = GenerateOrderCode;

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
            OrderCode = OrderCodeFactory(),
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Payments.Add(payment);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (IsOrderCodeCollision(ex))
            {
                // GenerateOrderCode() can draw the same value twice within the same millisecond
                // under concurrent creation; regenerate and retry a bounded number of times
                // instead of surfacing the raw unique-violation to the caller.
                if (attempt >= MaxOrderCodeAttempts)
                {
                    _logger.LogError(ex,
                        "Exhausted {MaxAttempts} order-code generation attempts for customer {CustomerId}.",
                        MaxOrderCodeAttempts, request.CustomerId);
                    throw new ConflictException("Could not generate a unique payment order code. Please try again.");
                }

                _logger.LogWarning(
                    "OrderCode collision on attempt {Attempt} for customer {CustomerId}; regenerating.",
                    attempt, request.CustomerId);
                payment.OrderCode = OrderCodeFactory();
            }
        }
        var orderCode = payment.OrderCode!.Value;

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

    internal static bool IsOrderCodeCollision(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? string.Empty;
        // Npgsql error code 23505 = unique_violation. Scoped to this specific constraint —
        // an unrelated unique-violation (e.g. idempotency key) should surface as-is, not
        // trigger a pointless order-code retry.
        return inner.Contains("23505") && inner.Contains("uq_payments_order_code");
    }
}
