namespace FinViet.Application.DTOs.Ai;

public record AiPreferenceDto(
    string CategorizationMode,
    decimal AutoCategorizationThreshold,
    bool DefaultHistoryEnabled,
    bool WeeklyReportEnabled,
    bool ShareBalances,
    bool ShareTransactions,
    bool ShareBudgets,
    bool ShareGoals,
    bool ShareReports,
    bool RagEnabled);
