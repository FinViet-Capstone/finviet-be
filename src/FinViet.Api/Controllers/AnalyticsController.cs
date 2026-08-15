using FinViet.Application.Common;
using FinViet.Application.DTOs.Analytics;
using FinViet.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinViet.Application.Features.Analytics.Queries.GetAnalyticsTrend;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAnalyticsSummaryQuery(), cancellationToken);
        return Ok(ApiResponse<AdminAnalyticsSummaryDto>.Ok(result));
    }

    [HttpGet("trend")]
    public async Task<ActionResult<ApiResponse<List<DailyMetricDto>>>> GetTrend(
        [FromQuery] string metric = "signups",
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAnalyticsTrendQuery(metric, days), cancellationToken);
        return Ok(ApiResponse<List<DailyMetricDto>>.Ok(result));
    }
}
