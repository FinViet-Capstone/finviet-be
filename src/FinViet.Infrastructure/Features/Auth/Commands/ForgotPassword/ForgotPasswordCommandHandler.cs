using FinViet.Application.Features.Auth.Commands.ForgotPassword;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.Features.Auth.Commands.ForgotPassword;

public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, string>
{
    private readonly FinVietDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;

    public ForgotPasswordCommandHandler(FinVietDbContext db, IEmailService email, IConfiguration config)
    { _db = db; _email = email; _config = config; }

    public async Task<string> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Email == request.Email.ToLower() && c.IsActive, cancellationToken);

        if (customer is null)
            return "If this email is registered, a reset link will be sent.";

        // Invalidate old unused reset tokens
        var oldTokens = await _db.EmailVerificationTokens
            .Where(t => t.CustomerId == customer.CustomerId &&
                        t.TokenType  == EmailTokenType.ResetPassword &&
                        t.UsedAt     == null)
            .ToListAsync(cancellationToken);

        foreach (var t in oldTokens) t.UsedAt = DateTime.UtcNow;

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = rawToken,
            TokenType  = EmailTokenType.ResetPassword,
            ExpiresAt  = DateTime.UtcNow.AddHours(1),
            CreatedAt  = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var frontendUrl = _config["AppSettings:FrontendUrl"] ?? "http://localhost:3000";
        await _email.SendPasswordResetEmailAsync(customer.Email, customer.FullName,
            $"{frontendUrl}/reset-password?token={rawToken}");

        return "If this email is registered, a reset link will be sent.";
    }
}
