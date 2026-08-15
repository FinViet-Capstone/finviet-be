namespace FinViet.Api.IntegrationTests.Tests;

/// <summary>
/// Capstone "Register content" requirements that have NO backend endpoint yet.
/// These are permanently skipped placeholders so the gap is visible in test reports.
/// Convert each to a real test once the endpoint is implemented.
/// </summary>
public class KnownGapsTests
{
    // Admin Dashboard — "System analytics: total users, daily active users, total
    // transactions logged, AI API call volume and estimated cost per day".
    // RESOLVED (partial): GET /api/analytics/summary + /api/analytics/trend now cover
    // total/active/new customers, transactions, wallets, budgets, and free/premium split
    // plus a daily signups/transactions trend — see AnalyticsTests.cs. AI API call volume/cost
    // is still not exposed (AiUsageEvent is written but has no admin-facing read endpoint).

    // Admin Dashboard — "Category correction log: view cases where users manually
    // overrode AI-suggested categories". Write path exists (AI override) but no read API.
    [Fact(Skip = "NOT IMPLEMENTED (Capstone Admin req): category correction log VIEW endpoint is missing.")]
    public void Admin_CategoryCorrectionLog_ViewEndpoint() { }

    // Admin Dashboard — "Announcement broadcast: send in-app notifications to all
    // users or targeted segments".
    [Fact(Skip = "NOT IMPLEMENTED (Capstone Admin req): announcement / broadcast endpoint is missing.")]
    public void Admin_AnnouncementBroadcast_Endpoint() { }

    // Admin Dashboard — "User account management: view registered users,
    // activate/deactivate accounts, reset passwords". Only deactivate(id) exists.
    [Fact(Skip = "PARTIAL (Capstone Admin req): list-users, re-activate, and admin reset-password endpoints are missing (only deactivate exists).")]
    public void Admin_UserManagement_ListActivateReset() { }

    // Mobile — "Notification center: push reminders for budget alerts, weekly report
    // availability, and savings goal milestones".
    [Fact(Skip = "NOT IMPLEMENTED (Capstone Mobile req): notification-center API (list/read notifications) is missing.")]
    public void Customer_NotificationCenter_Endpoint() { }
}
