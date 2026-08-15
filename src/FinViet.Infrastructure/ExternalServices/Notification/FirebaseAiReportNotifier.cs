using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.ExternalServices.Notification;

/// <summary>
/// FCM notifier for weekly AI reports. Mirrors <see cref="FirebaseBudgetAlertNotifier"/>:
/// topic-based delivery (customer-{id}), no-ops gracefully when Firebase isn't configured so
/// report generation never depends on push success.
/// </summary>
public class FirebaseAiReportNotifier : IAiReportNotifier
{
    private readonly FinVietDbContext _db;
    private readonly ILogger<FirebaseAiReportNotifier> _logger;
    private readonly bool _enabled;

    public FirebaseAiReportNotifier(
        FinVietDbContext db,
        IConfiguration config,
        ILogger<FirebaseAiReportNotifier> logger)
    {
        _db = db;
        _logger = logger;

        if (FirebaseApp.DefaultInstance is not null)
        {
            _enabled = true;
            return;
        }

        var credentialPath = config["Firebase:ServiceAccountJsonPath"];
        var projectId = config["Firebase:ProjectId"];

        try
        {
            if (!string.IsNullOrEmpty(credentialPath) && File.Exists(credentialPath))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credentialPath),
                    ProjectId = projectId ?? "finviet"
                });
                _enabled = true;
            }
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.GetApplicationDefault(),
                    ProjectId = projectId ?? "finviet"
                });
                _enabled = true;
            }
            else
            {
                _logger.LogWarning(
                    "Firebase push notifications are not configured. AI report notifications will be skipped.");
                _enabled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase messaging for AI report notifications.");
            _enabled = false;
        }
    }

    public async Task SendReportReadyAsync(
        Guid customerId,
        Guid reportId,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return;

        var notifyReport = await _db.CustomerSettings.AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .Select(s => (bool?)s.NotifReport)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!notifyReport)
            return;

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(new Message
            {
                Topic = $"customer-{customerId}",
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = title,
                    Body = message
                },
                Data = new Dictionary<string, string>
                {
                    ["type"] = "weekly_report",
                    ["customerId"] = customerId.ToString()
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Firebase AI report notification for customer {CustomerId}.", customerId);
        }
    }
}
