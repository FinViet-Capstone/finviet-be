using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Weekly Vietnamese narrative report. Maps to the v3 <c>ai_weekly_reports</c> table:
/// <c>report_text_vi</c> narrative, <c>week_start</c> (period end is derived as +6 days), and an
/// <c>is_read</c> flag. There is no score_id link in v3.
/// </summary>
public partial class AiWeeklyReport
{
    /// <summary>Maps to column <c>id</c>.</summary>
    public Guid ReportId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>Maps to column <c>report_text_vi</c>.</summary>
    public string Narrative { get; set; } = null!;

    /// <summary>Maps to column <c>week_start</c>. Week end = WeekStart + 6 days.</summary>
    public DateOnly WeekStart { get; set; }

    public bool IsRead { get; set; }

    public DateTime GeneratedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
