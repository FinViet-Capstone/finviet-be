using FinViet.Application.Common;
using FinViet.Application.DTOs.CategoryCorrections;
using FinViet.Application.Features.CategoryCorrections.Queries.GetCategoryCorrections;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/category-corrections")]
public class CategoryCorrectionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CategoryCorrectionsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CategoryCorrectionResponseDto>>>> GetCategoryCorrections(
        [FromQuery] CategoryCorrectionQueryDto query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCategoryCorrectionsQuery(query), cancellationToken);
        return Ok(ApiResponse<PagedResult<CategoryCorrectionResponseDto>>.Ok(result));
    }
}
