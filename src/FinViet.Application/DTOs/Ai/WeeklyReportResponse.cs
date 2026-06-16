namespace FinViet.Application.DTOs.Ai;

public class WeeklyReportResponse
{
    public Guid ReportId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Narrative { get; set; } = null!;
    public decimal? FinalScore { get; set; }
    public string? ColorBadge { get; set; }
    public DateTime GeneratedAt { get; set; }
}
