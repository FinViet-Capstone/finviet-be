using FinViet.Application.Common;
using FinViet.Application.DTOs.Scoring;
using FinViet.Application.Features.Scoring.Commands.UpdateScoringCriterion;
using FinViet.Application.Features.Scoring.Queries.GetScoringCriteria;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/scoring-criteria")]
public class ScoringCriteriaController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScoringCriteriaController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ScoringCriterionResponse>>>> GetScoringCriteria(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetScoringCriteriaQuery(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ScoringCriterionResponse>>.Ok(result));
    }

    [HttpPatch("{code}")]
    public async Task<ActionResult<ApiResponse<ScoringCriterionResponse>>> UpdateScoringCriterion(
        [FromRoute] string code,
        [FromBody] UpdateScoringCriterionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateScoringCriterionCommand(code, request.WeightWeekly, request.WeightMonthly),
            cancellationToken);

        return Ok(ApiResponse<ScoringCriterionResponse>.Ok(result, "Scoring criterion updated successfully"));
    }
}
