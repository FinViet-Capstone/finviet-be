using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ResendVerificationEmail;
using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Features.Auth;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Auth.Commands.ResendVerificationEmail;

public class ResendVerificationEmailCommandHandler : IRequestHandler<ResendVerificationEmailCommand, string>
{
    private const string GenericResponse =
        "If this email is registered and not yet verified, a new verification code has been sent.";

    private readonly FinVietDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendVerificationEmailCommandHandler> _logger;

    public ResendVerificationEmailCommandHandler(
        FinVietDbContext db,
        IEmailService emailService,
        IConfiguration config,
        ILogger<ResendVerificationEmailCommandHandler> logger)
    {
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<string> Handle(ResendVerificationEmailCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToLower().Trim();

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Email == normalizedEmail && c.IsActive, cancellationToken);

        if (customer is null)
            return GenericResponse;

        if (customer.IsEmailVerified)
            throw new BadRequestException("This email is already verified. You can log in directly.");

        var oldTokens = await _db.EmailVerificationTokens
            .Where(t => t.CustomerId == customer.CustomerId
                     && t.TokenType  == EmailTokenType.VerifyEmail
                     && t.UsedAt     == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var t in oldTokens) t.UsedAt = now;

        var code = await GenerateUniqueCodeAsync(cancellationToken);

        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = code,
            TokenType  = EmailTokenType.VerifyEmail,
            ExpiresAt  = now.AddHours(24),
            CreatedAt  = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailService.SendVerificationEmailAsync(customer.Email, customer.FullName, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resend verification email for {Email}. Code: {Code}",
                customer.Email, code);
            throw new BadRequestException(
                "Could not send verification email at this time. Please try again later.");
        }

        return GenericResponse;
    }

    /// <summary>Generate a 6-char code unique among active (unused, non-expired) verify tokens.</summary>
    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        string code;
        do
        {
            code = VerificationCode.Generate();
        }
        while (await _db.EmailVerificationTokens.AnyAsync(t =>
            t.Token == code && t.TokenType == EmailTokenType.VerifyEmail &&
            t.UsedAt == null && t.ExpiresAt > now, ct));
        return code;
    }
}
