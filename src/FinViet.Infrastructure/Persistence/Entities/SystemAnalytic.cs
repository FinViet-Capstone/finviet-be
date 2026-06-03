using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class SystemAnalytic
{
    public Guid AnalyticsId { get; set; }

    public Guid? AdminId { get; set; }

    public string MetricName { get; set; } = null!;

    public decimal MetricValue { get; set; }

    public DateTime? RecordedAt { get; set; }

    public virtual Admin? Admin { get; set; }
}
