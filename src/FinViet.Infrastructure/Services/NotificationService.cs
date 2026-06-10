using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
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
            GoalId = goalId,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PushFcmAsync(customerId, title, message, cancellationToken);
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
