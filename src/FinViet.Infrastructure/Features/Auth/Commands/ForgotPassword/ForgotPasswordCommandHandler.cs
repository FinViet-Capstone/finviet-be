using FinViet.Application.Features.Auth.Commands.ForgotPassword;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Features.Auth;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, string>
{
    private readonly FinVietDbContext _db;
    private readonly IEmailService _email;

    public ForgotPasswordCommandHandler(FinVietDbContext db, IEmailService email)
    { _db = db; _email = email; }

    public async Task<string> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Email == request.Email.ToLower() && c.IsActive, cancellationToken);

        if (customer is null)
            return "If this email is registered, a reset code will be sent.";

        // Invalidate old unused reset tokens
        var oldTokens = await _db.EmailVerificationTokens
            .Where(t => t.CustomerId == customer.CustomerId &&
                        t.TokenType  == EmailTokenType.ResetPassword &&
                        t.UsedAt     == null)
            .ToListAsync(cancellationToken);

        foreach (var t in oldTokens) t.UsedAt = DateTime.UtcNow;

        var now  = DateTime.UtcNow;
        var code = await GenerateUniqueCodeAsync(cancellationToken);

        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = code,
            TokenType  = EmailTokenType.ResetPassword,
            ExpiresAt  = now.AddHours(1),
            CreatedAt  = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _email.SendPasswordResetEmailAsync(customer.Email, customer.FullName, code);

        return "If this email is registered, a reset code will be sent.";
    }

    /// <summary>Generate a 6-char code unique among active (unused, non-expired) reset tokens.</summary>
    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        string code;
        do
        {
            code = VerificationCode.Generate();
        }
        while (await _db.EmailVerificationTokens.AnyAsync(t =>
            t.Token == code && t.TokenType == EmailTokenType.ResetPassword &&
            t.UsedAt == null && t.ExpiresAt > now, ct));
        return code;
    }
}
