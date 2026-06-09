using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.IncomeSources;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/income-sources")]
public class IncomeSourcesController : ControllerBase
{
    private readonly IIncomeSourceService _incomeSourceService;

    public IncomeSourcesController(IIncomeSourceService incomeSourceService)
    {
        _incomeSourceService = incomeSourceService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IncomeSourceResponse>>>> GetIncomeSources(
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var sources = await _incomeSourceService.GetIncomeSourcesAsync(customerId, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<IncomeSourceResponse>>.Ok(
            sources,
            "Income sources retrieved successfully"));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<IncomeSourceResponse>>> GetIncomeSourceById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var source = await _incomeSourceService.GetIncomeSourceByIdAsync(customerId, id, cancellationToken);

        if (source is null)
            return NotFound(ApiResponse<IncomeSourceResponse>.Fail("Income source not found."));

        return Ok(ApiResponse<IncomeSourceResponse>.Ok(
            source,
            "Income source retrieved successfully"));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<IncomeSourceResponse>>> CreateIncomeSource(
        [FromBody] CreateIncomeSourceRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var source = await _incomeSourceService.CreateIncomeSourceAsync(customerId, request, cancellationToken);

        return CreatedAtAction(
            nameof(GetIncomeSourceById),
            new { id = source.SourceId },
            ApiResponse<IncomeSourceResponse>.Ok(
                source,
                "Income source created successfully"));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ApiResponse<IncomeSourceResponse>>> UpdateIncomeSource(
        [FromRoute] Guid id,
        [FromBody] UpdateIncomeSourceRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var source = await _incomeSourceService.UpdateIncomeSourceAsync(customerId, id, request, cancellationToken);

        if (source is null)
            return NotFound(ApiResponse<IncomeSourceResponse>.Fail("Income source not found."));

        return Ok(ApiResponse<IncomeSourceResponse>.Ok(
            source,
            "Income source updated successfully"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteIncomeSource(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var deleted = await _incomeSourceService.DeleteIncomeSourceAsync(customerId, id, cancellationToken);

        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Income source not found."));

        return Ok(ApiResponse<object?>.Ok(
            null,
            "Income source deleted successfully"));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
