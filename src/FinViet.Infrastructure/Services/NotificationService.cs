using FinViet.Application.DTOs.Notifications;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Persists in-app notifications. FCM push is hooked in <see cref="PushFcmAsync"/>;
/// it is a no-op until device-token registration + FirebaseMessaging are wired up.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly FinVietDbContext _dbContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(FinVietDbContext dbContext, ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task NotifyAsync(
        Guid customerId,
        string title,
        string message,
        Guid? goalId = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            NotificationId = Guid.NewGuid(),
            CustomerId = customerId,
            Type = "announcement",
            Title = title,
            Message = message,
            EntityType = goalId.HasValue ? "goal" : null,
            EntityId = goalId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PushFcmAsync(customerId, title, message, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationResponse>> GetNotificationsAsync(
        Guid customerId,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId);

        if (unreadOnly)
            query = query.Where(n => n.IsRead != true);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationResponse
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                EntityType = n.EntityType,
                EntityId = n.EntityId,
                IsRead = n.IsRead == true,
                SentAt = n.CreatedAt
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
                n => n.NotificationId == notificationId && n.CustomerId == customerId,
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
            .Where(n => n.CustomerId == customerId && n.IsRead != true)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
            notification.IsRead = true;

        if (unread.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return unread.Count;
    }

    /// <summary>
    /// Sends an FCM push to the customer's registered devices.
    /// No device-token store exists yet, so this currently logs intent only.
    /// To enable: register device tokens, then call FirebaseMessaging.DefaultInstance.SendAsync.
    /// </summary>
    private Task PushFcmAsync(Guid customerId, string title, string message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "FCM push (pending device-token wiring) for customer {CustomerId}: {Title} - {Message}",
            customerId, title, message);
        return Task.CompletedTask;
    }
}
