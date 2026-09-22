using FinViet.Application.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    public WebhooksController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// PayOS webhook receiver. Returns 200 for all valid calls (including duplicates/unknown
    /// order codes) so PayOS stops retrying. Returns 400 only for signature verification failure.
    /// </summary>
    [HttpPost("payos")]
    public async Task<IActionResult> PayOS(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        await _mediator.Send(new ProcessPayOSWebhookCommand(rawBody), cancellationToken);
        return Ok();
    }
}
