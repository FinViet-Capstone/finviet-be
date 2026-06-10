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
}
