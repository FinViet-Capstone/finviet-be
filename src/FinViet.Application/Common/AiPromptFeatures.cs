namespace FinViet.Application.Common;

/// <summary>
/// Feature keys for admin-tunable AI prompt configs (ai_prompt_configs.feature_key).
/// Must stay in sync with the AiRequestContext feature names used for telemetry.
/// </summary>
public static class AiPromptFeatures
{
    public const string Chat = "chat";
    public const string WeeklyReport = "weekly_report";
    public const string ScoreComment = "score_comment";
    public const string Classification = "classification";

    public static readonly IReadOnlyList<string> All =
        [Chat, WeeklyReport, ScoreComment, Classification];

    public static bool IsKnown(string? featureKey) =>
        featureKey is not null && All.Contains(featureKey);
}
