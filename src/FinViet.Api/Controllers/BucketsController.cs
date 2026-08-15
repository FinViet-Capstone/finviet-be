using FinViet.Application.Common;
using FinViet.Application.DTOs.Buckets;
using FinViet.Application.Features.Buckets.Commands.UpdateBucket;
using FinViet.Application.Features.Buckets.Queries.GetBuckets;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/buckets")]
public class BucketsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BucketsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BucketResponse>>>> GetBuckets(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBucketsQuery(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BucketResponse>>.Ok(result));
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<ApiResponse<BucketResponse>>> UpdateBucket(
        [FromRoute] string id,
        [FromBody] UpdateBucketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateBucketCommand(id, request.NameVi, request.NameEn, request.Color, request.Icon, request.SortOrder),
            cancellationToken);

        return Ok(ApiResponse<BucketResponse>.Ok(result, "Bucket updated successfully"));
    }
}
