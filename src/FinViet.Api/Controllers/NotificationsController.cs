using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Notifications;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpPut("devices")]
    public async Task<ActionResult<ApiResponse<object?>>> RegisterDevice(
        [FromBody] RegisterNotificationDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await _notificationService.RegisterDeviceAsync(GetCustomerId(), request, cancellationToken);
        return Ok(ApiResponse<object?>.Ok(null, "Notification device registered"));
    }

    [HttpDelete("devices")]
    public async Task<ActionResult<ApiResponse<object?>>> UnregisterDevice(
        [FromBody] UnregisterNotificationDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await _notificationService.UnregisterDeviceAsync(
            GetCustomerId(),
            request.InstallationId,
            cancellationToken);
        return Ok(ApiResponse<object?>.Ok(null, "Notification device unregistered"));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NotificationResponse>>>> GetNotifications(
        [FromQuery] bool unread,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var notifications = await _notificationService.GetNotificationsAsync(customerId, unread, cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<NotificationResponse>>.Ok(
            notifications,
            "Notifications retrieved successfully"));
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<object?>>> MarkAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var marked = await _notificationService.MarkAsReadAsync(customerId, id, cancellationToken);

        if (!marked)
            return NotFound(ApiResponse<object?>.Fail("Notification not found."));

        return Ok(ApiResponse<object?>.Ok(null, "Notification marked as read"));
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        var count = await _notificationService.MarkAllAsReadAsync(customerId, cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { count }, "All notifications marked as read"));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
