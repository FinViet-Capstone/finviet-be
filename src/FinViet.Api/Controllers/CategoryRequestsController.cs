using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.CategoryRequests;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/category-requests")]
public class CategoryRequestsController : ControllerBase
{
    private readonly ICategoryRequestService _service;

    public CategoryRequestsController(ICategoryRequestService service)
    {
        _service = service;
    }

    // ── Customer endpoints ───────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<CategoryRequestResponse>>> Submit(
        [FromBody] CreateCategoryRequestRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetUserId();
        var result = await _service.SubmitAsync(customerId, request, cancellationToken);

        return Ok(ApiResponse<CategoryRequestResponse>.Ok(
            result,
            "Category request submitted successfully"));
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryRequestResponse>>>> GetMyRequests(
        CancellationToken cancellationToken)
    {
        var customerId = GetUserId();
        var result = await _service.GetMyRequestsAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CategoryRequestResponse>>.Ok(
            result,
            "Your category requests retrieved successfully"));
    }

    // ── Admin endpoints ──────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryRequestResponse>>>> GetRequests(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetRequestsAsync(status, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CategoryRequestResponse>>.Ok(
            result,
            "Category requests retrieved successfully"));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<CategoryRequestResponse>>> Approve(
        [FromRoute] Guid id,
        [FromBody] ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetUserId();
        var result = await _service.ApproveAsync(adminId, id, request, cancellationToken);

        if (result is null)
            return NotFound(ApiResponse<CategoryRequestResponse>.Fail("Category request not found."));

        return Ok(ApiResponse<CategoryRequestResponse>.Ok(
            result,
            "Category request approved successfully"));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<CategoryRequestResponse>>> Reject(
        [FromRoute] Guid id,
        [FromBody] ReviewCategoryRequestRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = GetUserId();
        var result = await _service.RejectAsync(adminId, id, request, cancellationToken);

        if (result is null)
            return NotFound(ApiResponse<CategoryRequestResponse>.Fail("Category request not found."));

        return Ok(ApiResponse<CategoryRequestResponse>.Ok(
            result,
            "Category request rejected successfully"));
    }

    private Guid GetUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var userId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid identifier claim.");

        return userId;
    }
}
