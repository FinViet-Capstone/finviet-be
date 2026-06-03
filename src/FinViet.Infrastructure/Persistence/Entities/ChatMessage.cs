using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class ChatMessage
{
    public Guid MessageId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? ReportId { get; set; }

    public string SenderType { get; set; } = null!;

    public string Content { get; set; } = null!;

    public DateTime? Timestamps { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual AiReport? Report { get; set; }
}
