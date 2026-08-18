using System;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AnnouncementBroadcast
{
    public Guid AnnouncementId { get; set; }

    public Guid AdminId { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string TargetSegment { get; set; } = "all";

    public string TargetLabel { get; set; } = null!;

    public int RecipientCount { get; set; }

    public DateTime SentAt { get; set; }

    public virtual Admin Admin { get; set; } = null!;
}
