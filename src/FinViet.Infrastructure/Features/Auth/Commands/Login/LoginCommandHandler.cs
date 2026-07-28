using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Auth;
using FinViet.Application.Features.Auth.Commands.Login;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Features.Profile;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponseDto>
{
    private readonly FinVietDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _config;

    public LoginCommandHandler(FinVietDbContext db, IJwtTokenService jwt, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
    }

    public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .Include(c => c.Setting)
            .FirstOrDefaultAsync(c => c.Email == request.Email.ToLower() && c.IsActive, cancellationToken);

        if (customer is null || !BCrypt.Net.BCrypt.Verify(request.Password, customer.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        if (!customer.IsEmailVerified)
            throw new BadRequestException("Please verify your email before logging in.");

        return await IssueTokensAsync(customer, cancellationToken);
    }

    public async Task<AuthResponseDto> IssueTokensAsync(Customer customer, CancellationToken cancellationToken)
    {
        var accessToken  = _jwt.GenerateAccessToken(customer.CustomerId, customer.Email, customer.FullName, "Customer");
        var refreshToken = _jwt.GenerateRefreshToken();
        var refreshDays  = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7");

        _db.RefreshTokens.Add(new Persistence.Entities.RefreshToken
        {
            TokenId    = Guid.NewGuid(),
            CustomerId = customer.CustomerId,
            Token      = refreshToken,
            ExpiresAt  = DateTime.UtcNow.AddDays(refreshDays),
            IsRevoked  = false,
            CreatedAt  = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var expiryMinutes = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "15");

        return new AuthResponseDto
        {
            AccessToken       = accessToken,
            RefreshToken      = refreshToken,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Profile = customer.ToProfileDto()
        };
    }
}
