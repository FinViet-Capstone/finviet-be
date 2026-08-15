namespace FinViet.Application.DTOs.Scoring;

public class ScoringCriterionResponse
{
    public string Code { get; set; } = string.Empty;
    public string CriterionName { get; set; } = string.Empty;
    public decimal WeightWeekly { get; set; }
    public decimal WeightMonthly { get; set; }
    public int Version { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
