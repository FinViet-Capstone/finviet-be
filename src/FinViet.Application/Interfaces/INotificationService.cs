using FinViet.Application.DTOs.Notifications;

namespace FinViet.Application.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Persists an in-app notification for a customer and (when configured) pushes it via FCM.
    /// Optionally linked to a saving goal.
    /// </summary>
    Task NotifyAsync(
        Guid customerId,
        string title,
        string message,
        Guid? goalId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a customer's notifications (newest first). Set <paramref name="unreadOnly"/> to filter to unread.</summary>
    Task<IReadOnlyList<NotificationResponse>> GetNotificationsAsync(
        Guid customerId,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a single notification as read. Returns false if it does not exist or belongs to another customer.</summary>
    Task<bool> MarkAsReadAsync(
        Guid customerId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks all of the customer's unread notifications as read. Returns the number updated.</summary>
    Task<int> MarkAllAsReadAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
