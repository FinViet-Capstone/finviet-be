using FinViet.Application.DTOs.Notifications;

namespace FinViet.Application.Interfaces;

public interface INotificationService
{
    /// <summary>Registers or rotates one authenticated app installation's Expo push token.</summary>
    Task RegisterDeviceAsync(
        Guid customerId,
        RegisterNotificationDeviceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one authenticated app installation. Returns false when it is not registered.</summary>
    Task<bool> UnregisterDeviceAsync(
        Guid customerId,
        string installationId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the canonical notification row, then sends a best-effort push.</summary>
    Task<NotificationResponse> NotifyAsync(
        Guid customerId,
        string type,
        string title,
        string message,
        string? entityType = null,
        Guid? entityId = null,
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
