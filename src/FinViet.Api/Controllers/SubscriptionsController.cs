using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Application.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Application.Features.SubscriptionPlans.Queries.ListSubscriptionPlans;
using FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/subscriptions")]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<object>>> Current(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCurrentSubscriptionQuery(User.GetCustomerId()), cancellationToken);
        return Ok(ApiResponse<object>.Ok(result!));
    }

    [HttpGet("plans")]
    public async Task<ActionResult<ApiResponse<object>>> Plans(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListSubscriptionPlansQuery(false), cancellationToken);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Poll after scanning QR; only the payment owner may read status.</summary>
    [HttpGet("payment-status/{orderCode:long}")]
    public async Task<ActionResult<ApiResponse<object>>> PaymentStatus(long orderCode, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPaymentStatusQuery(User.GetCustomerId(), orderCode), cancellationToken);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Creates a PayOS payment order; returns QR code data for the mobile app to render.</summary>
    [HttpPost("create-payment")]
    public async Task<ActionResult<ApiResponse<object>>> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreatePaymentCommand(User.GetCustomerId(), request.PlanId, idempotencyKey),
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(result));
    }
}

public record CreatePaymentRequest(Guid PlanId);
