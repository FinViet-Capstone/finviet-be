using FinViet.Application.Common;
using FinViet.Application.DTOs.Announcements;
using FinViet.Application.Features.Announcements.Queries.GetAnnouncements;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Announcements.Queries.GetAnnouncements;

public class GetAnnouncementsQueryHandler
    : IRequestHandler<GetAnnouncementsQuery, PagedResult<AnnouncementResponse>>
{
    private readonly FinVietDbContext _db;

    public GetAnnouncementsQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<PagedResult<AnnouncementResponse>> Handle(
        GetAnnouncementsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Query;
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;

        var query = _db.AnnouncementBroadcasts.AsNoTracking();

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AnnouncementResponse
            {
                Id = a.AnnouncementId,
                Title = a.Title,
                TargetLabel = a.TargetLabel,
                RecipientCount = a.RecipientCount,
                SentAt = a.SentAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AnnouncementResponse>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = items
        };
    }
}
