using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.ExternalServices.Notification;

public class NotificationBudgetAlertNotifier : IBudgetAlertNotifier
{
    private readonly FinVietDbContext _dbContext;
    private readonly INotificationService _notificationService;

    public NotificationBudgetAlertNotifier(
        FinVietDbContext dbContext,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    public async Task SendBudgetAlertAsync(
        Guid customerId,
        Guid budgetId,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        var isEnabled = await _dbContext.CustomerSettings
            .AsNoTracking()
            .Where(setting => setting.CustomerId == customerId)
            .Select(setting => (bool?)setting.NotifBudget)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!isEnabled)
            return;

        await _notificationService.NotifyAsync(
            customerId,
            "budget_alert",
            title,
            message,
            "budget",
            budgetId,
            cancellationToken);
    }
}
