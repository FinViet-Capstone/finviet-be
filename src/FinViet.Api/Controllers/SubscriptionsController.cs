using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.Features.Subscriptions.Commands.ProcessVNPayIpn;
using FinViet.Application.Features.Subscriptions.Commands.SubscribeToPlan;
using FinViet.Application.Features.Subscriptions.Queries.GetVNPayReturnStatus;
using FinViet.Application.Features.Subscriptions.Queries.GetSubscriptionPayment;
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

    /// <summary>Poll after scanning QR; only the payment owner may read its IPN-confirmed status.</summary>
    [HttpGet("payments/{paymentId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Payment(Guid paymentId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSubscriptionPaymentQuery(User.GetCustomerId(), paymentId), cancellationToken);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>Starts a premium subscription purchase; returns a VNPay redirect URL.</summary>
    [HttpPost("subscribe")]
    public async Task<ActionResult<ApiResponse<object>>> Subscribe(
        [FromBody] SubscribeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubscribeToPlanCommand(
                User.GetCustomerId(),
                request.PlanId,
                request.ReturnUrl,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0",
                idempotencyKey,
                request.BankCode),
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Browser return leg — informational only, never authoritative. See
    /// GetVNPayReturnStatusQueryHandler.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("vnpay/return")]
    public async Task<ActionResult<ApiResponse<object>>> VNPayReturn(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetVNPayReturnStatusQuery(ToDictionary(Request.Query)), cancellationToken);
        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// VNPay's server-to-server IPN — authoritative. Always returns HTTP 200 with VNPay's
    /// required {RspCode, Message} JSON shape (not the app's usual ApiResponse&lt;T&gt; envelope),
    /// since VNPay's retry/ack behavior is driven by that body's content, not the HTTP status.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("vnpay/ipn")]
    public async Task<IActionResult> VNPayIpn(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ProcessVNPayIpnCommand(ToDictionary(Request.Query)), cancellationToken);
        return Ok(result);
    }

    private static Dictionary<string, string> ToDictionary(IQueryCollection query) =>
        query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
}

public record SubscribeRequest(Guid PlanId, string ReturnUrl, string? BankCode = null);
