using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.ExternalServices.Notification;

public class NotificationAiReportNotifier : IAiReportNotifier
{
    private readonly FinVietDbContext _dbContext;
    private readonly INotificationService _notificationService;

    public NotificationAiReportNotifier(
        FinVietDbContext dbContext,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
    }

    public async Task SendReportReadyAsync(
        Guid customerId,
        Guid reportId,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        var isEnabled = await _dbContext.CustomerSettings
            .AsNoTracking()
            .Where(setting => setting.CustomerId == customerId)
            .Select(setting => (bool?)setting.NotifReport)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!isEnabled)
            return;

        await _notificationService.NotifyAsync(
            customerId,
            "weekly_report",
            title,
            message,
            "report",
            reportId,
            cancellationToken);
    }
}
