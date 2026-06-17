using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

// Flat recurring budgets (schema v2.1 §5 / APIs-List §6).
[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/budgets")]
public class BudgetsController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetsController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    // GET /api/budgets?month=YYYY-MM  (month optional → tháng hiện tại ICT)
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BudgetResponse>>>> GetBudgets(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetBudgetsAsync(customerId, month, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BudgetResponse>>.Ok(result, "Budgets retrieved successfully"));
    }

    // GET /api/budgets/buckets?month=YYYY-MM
    [HttpGet("buckets")]
    public async Task<ActionResult<ApiResponse<BucketSummaryListResponse>>> GetBucketSummary(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetBucketSummaryAsync(customerId, month, cancellationToken);

        return Ok(ApiResponse<BucketSummaryListResponse>.Ok(result, "Bucket summary retrieved successfully"));
    }

    // POST /api/budgets — upsert theo (customer, category, wallet)
    [HttpPost]
    public async Task<ActionResult<ApiResponse<BudgetResponse>>> UpsertBudget(
        [FromBody] UpsertBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.UpsertBudgetAsync(customerId, request, cancellationToken);

        return Ok(ApiResponse<BudgetResponse>.Ok(result, "Budget saved successfully"));
    }

    // PATCH /api/budgets/{id}
    [HttpPatch("{budgetId:guid}")]
    public async Task<ActionResult<ApiResponse<BudgetResponse>>> UpdateBudget(
        [FromRoute] Guid budgetId,
        [FromBody] UpdateBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.UpdateBudgetAsync(customerId, budgetId, request, cancellationToken);

        return Ok(ApiResponse<BudgetResponse>.Ok(result, "Budget updated successfully"));
    }

    // DELETE /api/budgets/{id}
    [HttpDelete("{budgetId:guid}")]
    public async Task<IActionResult> DeleteBudget(
        [FromRoute] Guid budgetId,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        await _budgetService.DeleteBudgetAsync(customerId, budgetId, cancellationToken);

        return NoContent();
    }
}
