using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.Features.Account.Commands.DeactivateAccount;
using FinViet.Application.Features.Account.Commands.DeleteAccount;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
[Produces("application/json")]
public class AccountController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator) => _mediator = mediator;

    /// <summary>Người dùng tự xóa tài khoản (soft delete). Tất cả refresh token bị thu hồi.</summary>
    [HttpDelete]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var result     = await _mediator.Send(new DeleteAccountCommand(customerId), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }

    /// <summary>Admin vô hiệu hóa tài khoản của một customer. (Role: Admin)</summary>
    [HttpPut("deactivate/{customerId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAccount(Guid customerId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeactivateAccountCommand(customerId), ct);
        return Ok(ApiResponse<string>.Ok(result));
    }
}