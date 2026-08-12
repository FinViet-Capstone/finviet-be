namespace FinViet.Application.DTOs.Ai;

public record FinancialContextResult(
    string Content,
    string DataPeriod,
    IReadOnlyList<ChatCitation> Citations,
    IReadOnlyList<string> Limitations);
