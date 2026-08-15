namespace FinViet.Infrastructure.Persistence.Entities;

public class NotificationDevice
{
    public Guid DeviceId { get; set; }

    public Guid CustomerId { get; set; }

    public string Token { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public string InstallationId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
