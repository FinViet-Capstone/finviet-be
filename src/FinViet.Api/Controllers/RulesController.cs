using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Rules;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

/// <summary>
/// Merchant-keyword auto-categorization rules (api_list #41–43).
/// Customer-scoped: each rule maps a beneficiary/merchant keyword to a category and is
/// applied retroactively on creation.
/// </summary>
[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/rules")]
[Produces("application/json")]
public class RulesController : ControllerBase
{
    private readonly IMerchantRuleService _rules;

    public RulesController(IMerchantRuleService rules) => _rules = rules;

    /// <summary>Danh sách rule gán danh mục tự động theo từ khóa thụ hưởng/merchant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RuleResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RuleResponse>>>> GetRules(CancellationToken ct)
    {
        var rules = await _rules.GetRulesAsync(GetCustomerId(), ct);
        return Ok(ApiResponse<IReadOnlyList<RuleResponse>>.Ok(rules, "Rules retrieved successfully"));
    }

    /// <summary>Tạo rule mới; hồi tố gán danh mục cho mọi giao dịch khớp từ khóa (bỏ qua chuyển quỹ).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateRuleResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CreateRuleResponse>>> CreateRule(
        [FromBody] CreateRuleRequest request, CancellationToken ct)
    {
        var result = await _rules.CreateRuleAsync(GetCustomerId(), request, ct);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<CreateRuleResponse>.Ok(result, "Rule created successfully"));
    }

    /// <summary>Xóa rule — hệ thống ngừng tự động gán từ khóa này.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteRule([FromRoute] Guid id, CancellationToken ct)
    {
        var deleted = await _rules.DeleteRuleAsync(GetCustomerId(), id, ct);
        if (!deleted)
            return NotFound(ApiResponse<object?>.Fail("Rule not found."));

        return Ok(ApiResponse<object?>.Ok(null, "Rule deleted successfully"));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
