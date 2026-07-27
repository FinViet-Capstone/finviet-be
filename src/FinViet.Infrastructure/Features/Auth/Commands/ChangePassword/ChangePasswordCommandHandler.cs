using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ChangePassword;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, string>
{
    private readonly FinVietDbContext _db;
    public ChangePasswordCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId && c.IsActive, cancellationToken);

        if (customer is null)
            throw new NotFoundException("Customer", request.CustomerId);

        if (customer.PasswordHash is null
            || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, customer.PasswordHash))
        {
            throw new BadRequestException("Current password is incorrect.");
        }

        customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        // Same security practice as ResetPasswordCommandHandler: force re-login on other sessions.
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.CustomerId == customer.CustomerId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var rt in activeTokens)
        {
            rt.IsRevoked = true;
            rt.RevokedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return "Password changed successfully.";
    }
}
