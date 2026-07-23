using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Auth;
using FinViet.Application.Features.Auth.Commands.RefreshToken;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Features.Profile;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly FinVietDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _config;

    public RefreshTokenCommandHandler(FinVietDbContext db, IJwtTokenService jwt, IConfiguration config)
    {
        _db = db; _jwt = jwt; _config = config;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var stored = await _db.RefreshTokens
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);

        if (stored is null)          throw new UnauthorizedException("Invalid refresh token.");
        if (stored.IsRevoked)        throw new UnauthorizedException("Refresh token has been revoked.");
        if (stored.ExpiresAt < DateTime.UtcNow) throw new UnauthorizedException("Refresh token has expired.");
        if (!stored.Customer.IsActive) throw new ForbiddenException("Account is deactivated.");

        // Rotation: revoke old
        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;

        var newAccess  = _jwt.GenerateAccessToken(stored.Customer.CustomerId, stored.Customer.Email, stored.Customer.FullName, "Customer");
        var newRefresh = _jwt.GenerateRefreshToken();
        var days       = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7");

        _db.RefreshTokens.Add(new Persistence.Entities.RefreshToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = stored.CustomerId,
            Token      = newRefresh,
            ExpiresAt  = DateTime.UtcNow.AddDays(days),
            IsRevoked  = false,
            CreatedAt  = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var mins     = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "15");
        var customer = stored.Customer;

        return new AuthResponseDto
        {
            AccessToken       = newAccess,
            RefreshToken      = newRefresh,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(mins),
            Profile = customer.ToProfileDto()
        };
    }
}
