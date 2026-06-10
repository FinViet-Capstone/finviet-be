using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.SavingGoals;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/saving-goals")]
public class SavingGoalsController : ControllerBase
{
    private readonly ISavingGoalService _savingGoalService;

    public SavingGoalsController(ISavingGoalService savingGoalService)
    {
        _savingGoalService = savingGoalService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SavingGoalResponse>>>> GetGoals(
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var goals = await _savingGoalService.GetGoalsAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<SavingGoalResponse>>.Ok(
            goals,
            "Saving goals retrieved successfully"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SavingGoalResponse>>> GetGoalById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var goal = await _savingGoalService.GetGoalByIdAsync(customerId, id, cancellationToken);

        if (goal is null)
            return NotFound(ApiResponse<SavingGoalResponse>.Fail("Saving goal not found."));

        return Ok(ApiResponse<SavingGoalResponse>.Ok(
            goal,
            "Saving goal retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<SavingGoalResponse>>> CreateGoal(
        [FromBody] CreateSavingGoalRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var goal = await _savingGoalService.CreateGoalAsync(customerId, request, cancellationToken);

        return CreatedAtAction(
            nameof(GetGoalById),
            new { id = goal.GoalId },
            ApiResponse<SavingGoalResponse>.Ok(goal, "Saving goal created successfully"));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SavingGoalResponse>>> UpdateGoal(
        [FromRoute] Guid id,
        [FromBody] UpdateSavingGoalRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var goal = await _savingGoalService.UpdateGoalAsync(customerId, id, request, cancellationToken);

        if (goal is null)
            return NotFound(ApiResponse<SavingGoalResponse>.Fail("Saving goal not found."));

        return Ok(ApiResponse<SavingGoalResponse>.Ok(
            goal,
            "Saving goal updated successfully"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteGoal(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var deleted = await _savingGoalService.DeleteGoalAsync(customerId, id, cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Saving goal not found."));

        return Ok(ApiResponse<object?>.Ok(null, "Saving goal deleted successfully"));
    }

    [HttpPost("{id:guid}/contribute")]
    public async Task<ActionResult<ApiResponse<SavingGoalResponse>>> Contribute(
        [FromRoute] Guid id,
        [FromBody] ContributeSavingGoalRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var goal = await _savingGoalService.ContributeAsync(customerId, id, request, cancellationToken);

        if (goal is null)
            return NotFound(ApiResponse<SavingGoalResponse>.Fail("Saving goal not found."));

        return Ok(ApiResponse<SavingGoalResponse>.Ok(
            goal,
            "Contribution added successfully"));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
