namespace FinViet.Domain.Enums;

/// <summary>
/// Maps to Postgres enum <c>notification_type</c>
/// (budget_alert, weekly_report, goal_milestone, announcement, sepay_sync_error).
/// </summary>
public enum NotificationType
{
    BudgetAlert,
    WeeklyReport,
    GoalMilestone,
    Announcement,
    SepaySyncError
}
