using MediatR;

namespace FinViet.Application.Features.Profile.Commands.UploadAvatar;

public record UploadAvatarCommand(
    Guid CustomerId,
    byte[] FileContent,
    string FileName,
    string ContentType
) : IRequest<string>; // returns new avatar URL
