namespace FinViet.Application.DTOs.Analytics;

public class DailyMetricDto
{
    /// <summary>"yyyy-MM-dd", UTC.</summary>
    public string Date { get; set; } = string.Empty;

    public int Count { get; set; }
}
