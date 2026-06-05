using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.VerifyEmail;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.VerifyEmail;

public class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand, string>
{
    private readonly FinVietDbContext _db;

    public VerifyEmailCommandHandler(FinVietDbContext db) => _db = db;

    public async Task<string> Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var token = await _db.EmailVerificationTokens
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t =>
                t.Token     == request.Token &&
                t.TokenType == "VERIFY_EMAIL",
                cancellationToken);

        if (token is null)
            throw new NotFoundException("Verification token not found or already used.");

        if (token.UsedAt is not null)
            throw new BadRequestException("This verification link has already been used.");

        if (token.ExpiresAt < DateTime.UtcNow)
            throw new BadRequestException("Verification link has expired. Please request a new one.");

        token.UsedAt                   = DateTime.UtcNow;
        token.Customer.IsEmailVerified = true;
        token.Customer.EmailVerifiedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return "Email verified successfully. You can now log in.";
    }
}
