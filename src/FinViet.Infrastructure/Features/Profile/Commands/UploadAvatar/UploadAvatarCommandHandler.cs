using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Profile.Commands.UploadAvatar;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Profile.Commands.UploadAvatar;

public class UploadAvatarCommandHandler : IRequestHandler<UploadAvatarCommand, string>
{
    private readonly FinVietDbContext _db;
    private readonly IAvatarService _avatar;

    public UploadAvatarCommandHandler(FinVietDbContext db, IAvatarService avatar)
    { _db = db; _avatar = avatar; }

    public async Task<string> Handle(UploadAvatarCommand request, CancellationToken cancellationToken)
    {
        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(request.ContentType.ToLower()))
            throw new BadRequestException("Only JPEG, PNG, and WebP images are allowed.");

        if (request.FileContent.Length > 5 * 1024 * 1024)
            throw new BadRequestException("Avatar file size must not exceed 5 MB.");

        if (!IsValidImageMagicBytes(request.FileContent, request.ContentType))
            throw new BadRequestException("File content does not match the declared image type.");

        var c = await _db.Customers
            .FirstOrDefaultAsync(x => x.CustomerId == request.CustomerId && x.IsActive, cancellationToken);

        if (c is null) throw new NotFoundException("Customer", request.CustomerId);

        if (!string.IsNullOrEmpty(c.AvatarUrl))
            await _avatar.DeleteAsync(c.AvatarUrl);

        var url = await _avatar.UploadAsync(request.FileContent, request.FileName, request.ContentType);
        c.AvatarUrl = url;

        await _db.SaveChangesAsync(cancellationToken);
        return url;
    }

    private static bool IsValidImageMagicBytes(byte[] content, string contentType)
    {
        if (content.Length < 4) return false;

        return contentType.ToLower() switch
        {
            // JPEG: FF D8 FF
            "image/jpeg" => content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
            // PNG: 89 50 4E 47
            "image/png"  => content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47,
            // WebP: RIFF....WEBP
            "image/webp" => content.Length >= 12
                            && content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
                            && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50,
            _ => false
        };
    }
}