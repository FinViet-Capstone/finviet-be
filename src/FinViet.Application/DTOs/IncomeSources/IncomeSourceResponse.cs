namespace FinViet.Application.DTOs.IncomeSources;

public class IncomeSourceResponse
{
    public Guid SourceId { get; set; }
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
}
