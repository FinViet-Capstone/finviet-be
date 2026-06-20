using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Notification
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid NotificationId { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>Postgres enum <c>notification_type</c> (NOT NULL). Defaults to 'announcement'.</summary>
    public string Type { get; set; } = "announcement";

    public string Title { get; set; } = null!;

    /// <summary>Maps to column <c>body</c> in the v3 schema.</summary>
    public string? Message { get; set; }

    /// <summary>Postgres enum <c>notification_entity_type</c> (budget/goal/report/wallet/system), nullable.</summary>
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public bool? IsRead { get; set; }

    /// <summary>Maps to column <c>sent_at</c> in the v3 schema.</summary>
    public DateTime? CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
