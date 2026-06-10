namespace FinViet.Application.DTOs.IncomeSources;

public class CreateIncomeSourceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
}
