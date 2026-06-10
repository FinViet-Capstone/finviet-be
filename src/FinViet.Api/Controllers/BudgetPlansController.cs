using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Budgets;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/budget-plans")]
public class BudgetPlansController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetPlansController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BudgetPlanResponse>>>> GetBudgetPlans(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetBudgetPlansAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BudgetPlanResponse>>.Ok(
            result,
            "Budget plans retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BudgetPlanResponse>>> CreateBudgetPlan(
        [FromBody] CreateBudgetPlanRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.CreateBudgetPlanAsync(
            customerId,
            request,
            cancellationToken);

        return Ok(ApiResponse<BudgetPlanResponse>.Ok(
            result,
            "Budget plan created successfully"));
    }

    [HttpGet("{planId:guid}/tracking")]
    public async Task<ActionResult<ApiResponse<BudgetTrackingResponse>>> GetBudgetTracking(
        [FromRoute] Guid planId,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetBudgetTrackingAsync(
            customerId,
            planId,
            cancellationToken);

        return Ok(ApiResponse<BudgetTrackingResponse>.Ok(
            result,
            "Budget tracking retrieved successfully"));
    }

    [HttpGet("current/tracking")]
    public async Task<ActionResult<ApiResponse<BudgetTrackingResponse>>> GetCurrentBudgetTracking(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetCurrentBudgetTrackingAsync(
            customerId,
            cancellationToken);

        return Ok(ApiResponse<BudgetTrackingResponse>.Ok(
            result,
            "Current budget tracking retrieved successfully"));
    }

    [HttpPatch("category-budgets/{categoryBudgetId:guid}")]
    public async Task<ActionResult<ApiResponse<CategoryBudgetResponse>>> UpdateCategoryBudget(
        [FromRoute] Guid categoryBudgetId,
        [FromBody] UpdateCategoryBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.UpdateCategoryBudgetAsync(
            customerId,
            categoryBudgetId,
            request,
            cancellationToken);

        return Ok(ApiResponse<CategoryBudgetResponse>.Ok(
            result,
            "Category budget updated successfully"));
    }

    [HttpDelete("{planId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBudgetPlan(
        [FromRoute] Guid planId,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.DeleteBudgetPlanAsync(
            customerId,
            planId,
            cancellationToken);

        return Ok(ApiResponse<bool>.Ok(
            result,
            "Budget plan deleted successfully"));
    }

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BudgetHistoryResponse>>>> GetBudgetHistory(
        [FromQuery] int year,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.GetBudgetHistoryAsync(
            customerId,
            year,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BudgetHistoryResponse>>.Ok(
            result,
            "Budget history retrieved successfully"));
    }

    [HttpPost("reset-current-month")]
    public async Task<ActionResult<ApiResponse<BudgetPlanResponse>>> ResetCurrentMonthBudget(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var result = await _budgetService.ResetCurrentMonthBudgetAsync(
            customerId,
            cancellationToken);

        return Ok(ApiResponse<BudgetPlanResponse>.Ok(
            result,
            "Current month budget reset successfully"));
    }
}
