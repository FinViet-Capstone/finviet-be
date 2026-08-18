using FinViet.Application.DTOs.Announcements;
using MediatR;

namespace FinViet.Application.Features.Announcements.Commands.CreateAnnouncement;

public record CreateAnnouncementCommand(Guid AdminId, CreateAnnouncementRequest Request)
    : IRequest<AnnouncementResponse>;
