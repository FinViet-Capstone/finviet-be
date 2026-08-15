namespace FinViet.Application.Interfaces;

public interface INotificationPushSender
{
    Task<NotificationPushResult> SendAsync(
        IReadOnlyList<NotificationPushDevice> devices,
        NotificationPushMessage message,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationPushDevice(Guid DeviceId, string Token);

public sealed record NotificationPushMessage(
    Guid NotificationId,
    string Type,
    string Title,
    string Message,
    string? EntityType,
    Guid? EntityId);

public sealed record NotificationPushResult(IReadOnlyCollection<Guid> InvalidDeviceIds)
{
    public static NotificationPushResult Empty { get; } = new(Array.Empty<Guid>());
}
