using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiReport
{
    public Guid ReportId { get; set; }

    public Guid? CustomerId { get; set; }

    public DateTime? GeneratedDate { get; set; }

    public string? Content { get; set; }

    public int? SpendingScore { get; set; }

    public virtual ICollection<AiReportDetail> AiReportDetails { get; set; } = new List<AiReportDetail>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
