using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.UpdateAiPreferences;

public record UpdateAiPreferencesCommand(
    Guid CustomerId,
    string? CategorizationMode = null,
    decimal? AutoCategorizationThreshold = null,
    bool? DefaultHistoryEnabled = null,
    bool? WeeklyReportEnabled = null,
    bool? ShareBalances = null,
    bool? ShareTransactions = null,
    bool? ShareBudgets = null,
    bool? ShareGoals = null,
    bool? ShareReports = null,
    bool? RagEnabled = null) : IRequest<AiPreferenceDto>;
