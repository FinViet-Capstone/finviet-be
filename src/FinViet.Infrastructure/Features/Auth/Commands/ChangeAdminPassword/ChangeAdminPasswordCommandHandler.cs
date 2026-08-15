using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ChangeAdminPassword;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.ChangeAdminPassword;

public class ChangeAdminPasswordCommandHandler : IRequestHandler<ChangeAdminPasswordCommand, string>
{
    private readonly FinVietDbContext _db;
    public ChangeAdminPasswordCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(ChangeAdminPasswordCommand request, CancellationToken cancellationToken)
    {
        var admin = await _db.Admins
            .FirstOrDefaultAsync(a => a.AdminId == request.AdminId, cancellationToken);

        if (admin is null)
            throw new NotFoundException("Admin", request.AdminId);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, admin.PasswordHash))
            throw new BadRequestException("Current password is incorrect.");

        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        await _db.SaveChangesAsync(cancellationToken);
        return "Password changed successfully.";
    }
}
