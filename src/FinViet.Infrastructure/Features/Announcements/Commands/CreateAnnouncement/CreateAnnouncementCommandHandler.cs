using FinViet.Application.DTOs.Announcements;
using FinViet.Application.Features.Announcements.Commands.CreateAnnouncement;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Announcements.Commands.CreateAnnouncement;

public class CreateAnnouncementCommandHandler
    : IRequestHandler<CreateAnnouncementCommand, AnnouncementResponse>
{
    // Chunked to keep each SaveChanges/INSERT reasonably sized for a ~12k+ customer base instead
    // of tracking every notification in one giant batch.
    private const int BatchSize = 1000;
    private const string AllTargetLabel = "Tất cả";

    private readonly FinVietDbContext _db;

    public CreateAnnouncementCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<AnnouncementResponse> Handle(
        CreateAnnouncementCommand request,
        CancellationToken cancellationToken)
    {
        var title = request.Request.Title.Trim();
        var message = request.Request.Message.Trim();

        // "all" only sends to active customers — locked-out/deactivated accounts are excluded.
        var customerIds = await _db.Customers
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.CustomerId)
            .ToListAsync(cancellationToken);

        var sentAt = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < customerIds.Count; offset += BatchSize)
        {
            var batch = customerIds
                .Skip(offset)
                .Take(BatchSize)
                .Select(customerId => new Notification
                {
                    NotificationId = Guid.NewGuid(),
                    CustomerId = customerId,
                    Type = "announcement",
                    Title = title,
                    Message = message,
                    IsRead = false,
                    CreatedAt = sentAt
                });

            _db.Notifications.AddRange(batch);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var broadcast = new AnnouncementBroadcast
        {
            AnnouncementId = Guid.NewGuid(),
            AdminId = request.AdminId,
            Title = title,
            Message = message,
            TargetSegment = request.Request.TargetSegment,
            TargetLabel = AllTargetLabel,
            RecipientCount = customerIds.Count,
            SentAt = sentAt
        };
        _db.AnnouncementBroadcasts.Add(broadcast);
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Map(broadcast);
    }

    private static AnnouncementResponse Map(AnnouncementBroadcast broadcast) => new()
    {
        Id = broadcast.AnnouncementId,
        Title = broadcast.Title,
        TargetLabel = broadcast.TargetLabel,
        RecipientCount = broadcast.RecipientCount,
        SentAt = broadcast.SentAt
    };
}
