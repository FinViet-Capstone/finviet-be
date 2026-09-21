using FinViet.Application.Common;
using FinViet.Application.Features.Subscriptions.Commands.ConfirmPayOSWebhook;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/payos")]
public sealed class AdminPayOSController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminPayOSController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// One-time-per-environment call registering this app's webhook URL with payOS, since payOS
    /// sends no webhooks until the URL is confirmed. Falls back to the configured
    /// <c>PayOS:WebhookUrl</c> when the request omits one.
    /// </summary>
    [HttpPost("confirm-webhook")]
    public async Task<ActionResult<ApiResponse<object>>> ConfirmWebhook(
        [FromBody] ConfirmWebhookRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ConfirmPayOSWebhookCommand(request?.WebhookUrl),
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(result));
    }
}

public record ConfirmWebhookRequest(string? WebhookUrl);
