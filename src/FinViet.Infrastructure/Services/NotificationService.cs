using FinViet.Application.DTOs.Notifications;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly FinVietDbContext _dbContext;
    private readonly INotificationPushSender _pushSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        FinVietDbContext dbContext,
        INotificationPushSender pushSender,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _pushSender = pushSender;
        _logger = logger;
    }

    public async Task RegisterDeviceAsync(
        Guid customerId,
        RegisterNotificationDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.Token.Trim();
        var installationId = request.InstallationId.Trim();
        var platform = request.Platform.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var tokenOwner = await _dbContext.NotificationDevices
            .FirstOrDefaultAsync(device => device.Token == token, cancellationToken);
        if (tokenOwner is not null
            && (tokenOwner.CustomerId != customerId || tokenOwner.InstallationId != installationId))
        {
            _dbContext.NotificationDevices.Remove(tokenOwner);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var device = await _dbContext.NotificationDevices
            .FirstOrDefaultAsync(
                item => item.CustomerId == customerId && item.InstallationId == installationId,
                cancellationToken);

        if (device is null)
        {
            var newDevice = new NotificationDevice
            {
                DeviceId = Guid.NewGuid(),
                CustomerId = customerId,
                Token = token,
                Platform = platform,
                InstallationId = installationId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.NotificationDevices.Add(newDevice);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (IsCustomerInstallationConflict(ex))
            {
                // Lost a race with a concurrent registration for the same (customerId,
                // installationId) — e.g. an app-foreground retry — that committed between our
                // read above and this insert. Fall through to update the row the other request
                // just created instead of surfacing this as a 500.
                _dbContext.Entry(newDevice).State = EntityState.Detached;
                device = await _dbContext.NotificationDevices
                    .FirstAsync(
                        item => item.CustomerId == customerId && item.InstallationId == installationId,
                        cancellationToken);
            }
        }

        device.Token = token;
        device.Platform = platform;
        device.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsCustomerInstallationConflict(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? string.Empty;
        // Npgsql error code 23505 = unique_violation. Scoped to this specific constraint —
        // uq_notification_devices_token can also fire on insert here (handled above by removing
        // the stale token owner first) and needs different handling than a re-fetch-and-update.
        return inner.Contains("23505") && inner.Contains("uq_notification_devices_customer_installation");
    }

    public async Task<bool> UnregisterDeviceAsync(
        Guid customerId,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedInstallationId = installationId.Trim();
        var device = await _dbContext.NotificationDevices
            .FirstOrDefaultAsync(
                item => item.CustomerId == customerId
                    && item.InstallationId == normalizedInstallationId,
                cancellationToken);
        if (device is null)
            return false;

        _dbContext.NotificationDevices.Remove(device);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<NotificationResponse> NotifyAsync(
        Guid customerId,
        string type,
        string title,
        string message,
        string? entityType = null,
        Guid? entityId = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            NotificationId = Guid.NewGuid(),
            CustomerId = customerId,
            Type = type,
            Title = title,
            Message = message,
            EntityType = entityType,
            EntityId = entityId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PushBestEffortAsync(notification, cancellationToken);
        return Map(notification);
    }

    public async Task<IReadOnlyList<NotificationResponse>> GetNotificationsAsync(
        Guid customerId,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.CustomerId == customerId);

        if (unreadOnly)
            query = query.Where(notification => notification.IsRead != true);

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(notification => new NotificationResponse
            {
                NotificationId = notification.NotificationId,
                Type = notification.Type,
                Title = notification.Title,
                Message = notification.Message,
                EntityType = notification.EntityType,
                EntityId = notification.EntityId,
                IsRead = notification.IsRead == true,
                SentAt = notification.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkAsReadAsync(
        Guid customerId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(
                item => item.NotificationId == notificationId && item.CustomerId == customerId,
                cancellationToken);
        if (notification is null)
            return false;

        if (notification.IsRead != true)
        {
            notification.IsRead = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var unread = await _dbContext.Notifications
            .Where(notification => notification.CustomerId == customerId && notification.IsRead != true)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
            notification.IsRead = true;

        if (unread.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return unread.Count;
    }

    private async Task PushBestEffortAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        var devices = await _dbContext.NotificationDevices
            .AsNoTracking()
            .Where(device => device.CustomerId == notification.CustomerId)
            .Select(device => new NotificationPushDevice(device.DeviceId, device.Token))
            .ToListAsync(cancellationToken);
        if (devices.Count == 0)
            return;

        try
        {
            var result = await _pushSender.SendAsync(
                devices,
                new NotificationPushMessage(
                    notification.NotificationId,
                    notification.Type,
                    notification.Title,
                    notification.Message ?? string.Empty,
                    notification.EntityType,
                    notification.EntityId),
                cancellationToken);

            if (result.InvalidDeviceIds.Count == 0)
                return;

            var invalidDevices = await _dbContext.NotificationDevices
                .Where(device => result.InvalidDeviceIds.Contains(device.DeviceId))
                .ToListAsync(cancellationToken);
            _dbContext.NotificationDevices.RemoveRange(invalidDevices);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Push delivery failed after notification {NotificationId} was persisted.",
                notification.NotificationId);
        }
    }

    private static NotificationResponse Map(Notification notification)
        => new()
        {
            NotificationId = notification.NotificationId,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            EntityType = notification.EntityType,
            EntityId = notification.EntityId,
            IsRead = notification.IsRead == true,
            SentAt = notification.CreatedAt
        };
}
