using FinViet.Application.Common;
using FinViet.Application.DTOs.Announcements;
using MediatR;

namespace FinViet.Application.Features.Announcements.Queries.GetAnnouncements;

public record GetAnnouncementsQuery(AnnouncementQueryDto Query)
    : IRequest<PagedResult<AnnouncementResponse>>;
