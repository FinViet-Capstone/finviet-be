using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/budgets")]
[Route("v1/budgets")]
public class BudgetsController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetsController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BudgetResponse>>>> GetBudgets(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var result = await _budgetService.GetBudgetsAsync(
            User.GetCustomerId(),
            month,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BudgetResponse>>.Ok(
            result,
            "Budgets retrieved successfully"));
    }

    [HttpGet("buckets")]
    public async Task<ActionResult<ApiResponse<BucketSummaryListResponse>>> GetBudgetBuckets(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var result = await _budgetService.GetBudgetBucketsAsync(
            User.GetCustomerId(),
            month,
            cancellationToken);

        return Ok(ApiResponse<BucketSummaryListResponse>.Ok(
            result,
            "Budget buckets retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BudgetResponse>>> UpsertBudget(
        [FromBody] UpsertBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _budgetService.UpsertBudgetAsync(
            User.GetCustomerId(),
            request,
            cancellationToken);

        return Ok(ApiResponse<BudgetResponse>.Ok(
            result,
            "Budget saved successfully"));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<BudgetResponse>>> UpdateBudget(
        [FromRoute] Guid id,
        [FromBody] UpdateBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _budgetService.UpdateBudgetAsync(
            User.GetCustomerId(),
            id,
            request,
            cancellationToken);

        return Ok(ApiResponse<BudgetResponse>.Ok(
            result,
            "Budget updated successfully"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBudget(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _budgetService.DeleteBudgetAsync(
            User.GetCustomerId(),
            id,
            cancellationToken);

        return NoContent();
    }
}
