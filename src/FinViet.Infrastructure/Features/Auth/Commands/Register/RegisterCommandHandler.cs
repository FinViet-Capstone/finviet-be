using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Auth.Commands.Register;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, string>
{
    private readonly FinVietDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        FinVietDbContext db,
        IEmailService emailService,
        IConfiguration config,
        ILogger<RegisterCommandHandler> logger)
    {
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<string> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToLower().Trim();

        var exists = await _db.Customers
            .AnyAsync(c => c.Email == normalizedEmail, cancellationToken);

        if (exists)
            throw new ConflictException($"Email '{normalizedEmail}' is already registered.");

        var customer = new Customer
        {
            CustomerId      = Guid.NewGuid(),
            FullName        = request.FullName.Trim(),
            Email           = normalizedEmail,
            PasswordHash    = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Status          = "ACTIVE",
            IsEmailVerified = false,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow
        };

        _db.Customers.Add(customer);

        var rawToken    = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var verifyToken = new EmailVerificationToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = rawToken,
            TokenType  = "VERIFY_EMAIL",
            ExpiresAt  = DateTime.UtcNow.AddHours(24),
            CreatedAt  = DateTime.UtcNow
        };

        _db.EmailVerificationTokens.Add(verifyToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException($"Email '{normalizedEmail}' is already registered.");
        }

        var frontendUrl = _config["AppSettings:FrontendUrl"] ?? "http://localhost:3000";
        var verifyUrl   = $"{frontendUrl}/verify-email?token={rawToken}";

        try
        {
            await _emailService.SendVerificationEmailAsync(customer.Email, customer.FullName, verifyUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Account {Email} created but verification email failed to send. Token: {Token}",
                customer.Email, rawToken);

            return "Registration successful, but the verification email could not be sent. " +
                   "Please contact support or use the resend endpoint.";
        }

        return "Registration successful. Please check your email to verify your account.";
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? string.Empty;
        // Npgsql error code 23505 = unique_violation
        return inner.Contains("23505") || inner.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}