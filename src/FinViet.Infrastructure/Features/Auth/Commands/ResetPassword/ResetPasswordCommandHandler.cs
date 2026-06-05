using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ResetPassword;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, string>
{
    private readonly FinVietDbContext _db;
    public ResetPasswordCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new BadRequestException("Passwords do not match.");

        var token = await _db.EmailVerificationTokens
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t =>
                t.Token     == request.Token &&
                t.TokenType == "RESET_PASSWORD",
                cancellationToken);

        if (token is null)                     throw new NotFoundException("Reset token not found.");
        if (token.UsedAt is not null)          throw new BadRequestException("This reset link has already been used.");
        if (token.ExpiresAt < DateTime.UtcNow) throw new BadRequestException("Reset link has expired.");

        token.Customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        token.UsedAt                = DateTime.UtcNow;

        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.CustomerId == token.CustomerId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens) { rt.IsRevoked = true; rt.RevokedAt = DateTime.UtcNow; }

        await _db.SaveChangesAsync(cancellationToken);
        return "Password has been reset successfully. Please log in with your new password.";
    }
}
