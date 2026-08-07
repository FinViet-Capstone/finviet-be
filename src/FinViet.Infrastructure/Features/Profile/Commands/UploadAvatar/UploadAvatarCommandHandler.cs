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
        AvatarValidationRules.Validate(request.FileContent, request.ContentType);

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
}