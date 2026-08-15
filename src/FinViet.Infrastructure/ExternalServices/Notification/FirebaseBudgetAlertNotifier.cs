using FinViet.Application.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.ExternalServices;

public class FirebaseBudgetAlertNotifier : IBudgetAlertNotifier
{
    private readonly ILogger<FirebaseBudgetAlertNotifier> _logger;
    private readonly bool _enabled;

    public FirebaseBudgetAlertNotifier(
        IConfiguration config,
        ILogger<FirebaseBudgetAlertNotifier> logger)
    {
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
                    "Firebase push notifications are not configured. Budget alerts will only be stored in the database.");
                _enabled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase messaging for budget alerts.");
            _enabled = false;
        }
    }

    public async Task SendBudgetAlertAsync(
        Guid customerId,
        Guid budgetId,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return;

        try
        {
            // Topic-based delivery avoids introducing a new device-token table in this patch set.
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
                    ["type"] = "budget_alert",
                    ["customerId"] = customerId.ToString()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Firebase budget alert for customer {CustomerId}.", customerId);
        }
    }
}
