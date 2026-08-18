using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Announcements;
using FinViet.Application.Features.Announcements.Commands.CreateAnnouncement;
using FinViet.Application.Features.Announcements.Queries.GetAnnouncements;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/announcements")]
public class AdminAnnouncementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminAnnouncementsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Fans out a Notification row to every active customer and records one broadcast history row.</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AnnouncementResponse>>> CreateAnnouncement(
        [FromBody] CreateAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateAnnouncementCommand(GetAdminId(), request),
            cancellationToken);

        return Ok(ApiResponse<AnnouncementResponse>.Ok(result, "Announcement sent"));
    }

    /// <summary>Lists past broadcasts (newest first) for the admin "Announcement history" screen.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AnnouncementResponse>>>> GetAnnouncements(
        [FromQuery] AnnouncementQueryDto query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAnnouncementsQuery(query), cancellationToken);
        return Ok(ApiResponse<PagedResult<AnnouncementResponse>>.Ok(result));
    }

    private Guid GetAdminId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var adminId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid admin identifier claim.");

        return adminId;
    }
}
