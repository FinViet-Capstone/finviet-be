using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class AiReportDetail
{
    public Guid DetailId { get; set; }

    public Guid? ReportId { get; set; }

    public Guid? CriterionId { get; set; }

    public int? Score { get; set; }

    public virtual ScoringCriterion? Criterion { get; set; }

    public virtual AiReport? Report { get; set; }
}
