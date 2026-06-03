using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class ScoringCriterion
{
    public Guid CriterionId { get; set; }

    public string CriterionName { get; set; } = null!;

    public decimal Weight { get; set; }

    public string? Formula { get; set; }

    public virtual ICollection<AiReportDetail> AiReportDetails { get; set; } = new List<AiReportDetail>();
}
