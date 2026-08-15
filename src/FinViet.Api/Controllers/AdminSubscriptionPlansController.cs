using FinViet.Application.Common;
using FinViet.Application.Features.SubscriptionPlans.Commands.CreateSubscriptionPlan;
using FinViet.Application.Features.SubscriptionPlans.Commands.DiscontinueSubscriptionPlan;
using FinViet.Application.Features.SubscriptionPlans.Commands.UpdateSubscriptionPlan;
using FinViet.Application.Features.SubscriptionPlans.Queries.ListSubscriptionPlans;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/subscription-plans")]
public sealed class AdminSubscriptionPlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminSubscriptionPlansController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> List(
        [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSubscriptionPlansQuery(includeInactive), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> Create(
        [FromBody] CreatePlanRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateSubscriptionPlanCommand(
                request.Code, request.Name, request.Price, request.BillingIntervalMonths, request.Features),
            ct);
        return StatusCode(201, ApiResponse<object>.Ok(result));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Update(
        [FromRoute] Guid id, [FromBody] UpdatePlanRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateSubscriptionPlanCommand(
                id, request.Name, request.Price, request.BillingIntervalMonths, request.Features),
            ct);
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPatch("{id:guid}/discontinue")]
    public async Task<ActionResult<ApiResponse<object>>> Discontinue([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DiscontinueSubscriptionPlanCommand(id), ct);
        return Ok(ApiResponse<object>.Ok(result));
    }
}

public record CreatePlanRequest(string Code, string Name, decimal Price, short BillingIntervalMonths, string[] Features);
public record UpdatePlanRequest(string Name, decimal Price, short BillingIntervalMonths, string[] Features);
