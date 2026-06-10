using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.ResendVerificationEmail;
using FinViet.Application.Interfaces;
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
        "If this email is registered and not yet verified, a new verification link has been sent.";

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
                     && t.TokenType  == "VERIFY_EMAIL"
                     && t.UsedAt     == null)
            .ToListAsync(cancellationToken);

        foreach (var t in oldTokens) t.UsedAt = DateTime.UtcNow;

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        _db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = rawToken,
            TokenType  = "VERIFY_EMAIL",
            ExpiresAt  = DateTime.UtcNow.AddHours(24),
            CreatedAt  = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var backendUrl = _config["AppSettings:BackendUrl"] ?? "https://localhost:5001";
        var verifyUrl  = $"{backendUrl.TrimEnd('/')}/api/auth/verify-email?token={rawToken}";

        try
        {
            await _emailService.SendVerificationEmailAsync(customer.Email, customer.FullName, verifyUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resend verification email for {Email}. Token: {Token}",
                customer.Email, rawToken);
            throw new BadRequestException(
                "Could not send verification email at this time. Please try again later.");
        }

        return GenericResponse;
    }
}
