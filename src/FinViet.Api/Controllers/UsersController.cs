using FinViet.Application.Common;
using FinViet.Application.DTOs.Users;
using FinViet.Application.Features.Users.Queries.GetUsers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserResponseDto>>>> GetUsers(
        [FromQuery] UserQueryDto query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUsersQuery(query), cancellationToken);
        return Ok(ApiResponse<PagedResult<UserResponseDto>>.Ok(result));
    }
}
