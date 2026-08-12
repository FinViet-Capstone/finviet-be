using FinViet.Application.DTOs.Ai;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Features.Profile;

internal static class AiPreferenceMapper
{
    internal static AiPreferenceDto Map(AiCustomerPreference preference) => new(
        preference.CategorizationMode,
        preference.AutoCategorizationThreshold,
        preference.DefaultHistoryEnabled,
        preference.WeeklyReportEnabled,
        preference.ShareBalances,
        preference.ShareTransactions,
        preference.ShareBudgets,
        preference.ShareGoals,
        preference.ShareReports,
        preference.RagEnabled);

    internal static AiPreferenceDto Default() => new(
        "suggest_only",
        0.85m,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true);
}
