using FinViet.Application.Common;
using FinViet.Application.DTOs.Admins;
using FinViet.Application.Features.Admins.Commands.CreateAdmin;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admins")]
public class AdminsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly FinVietDbContext _db;

    public AdminsController(IMediator mediator, FinVietDbContext db)
    {
        _mediator = mediator;
        _db = db;
    }

    /// <summary>Danh sách toàn bộ quản trị viên.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminResponse>>>> GetAdmins(CancellationToken cancellationToken)
    {
        var admins = await _db.Admins
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AdminResponse(a.AdminId, a.Username, a.Email, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<AdminResponse>>.Ok(admins, "Admins retrieved successfully"));
    }

    /// <summary>Tạo tài khoản quản trị viên mới (chỉ admin đã đăng nhập mới thực hiện được).</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminResponse>>> CreateAdmin(
        [FromBody] CreateAdminRequest request,
        CancellationToken cancellationToken)
    {
        var admin = await _mediator.Send(
            new CreateAdminCommand(request.Username, request.Email, request.Password), cancellationToken);

        return Ok(ApiResponse<AdminResponse>.Ok(admin, "Admin created successfully"));
    }
}
