using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class ScoringCriterion
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid CriterionId { get; set; }

    public string Code { get; set; } = null!;

    /// <summary>Maps to column <c>name_vi</c> in the v3 schema.</summary>
    public string CriterionName { get; set; } = null!;

    public decimal WeightWeekly { get; set; }

    public decimal WeightMonthly { get; set; }

    public int Version { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
