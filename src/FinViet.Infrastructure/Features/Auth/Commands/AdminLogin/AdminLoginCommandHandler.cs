using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.DTOs.Auth;
using FinViet.Application.Features.Auth.Commands.AdminLogin;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinViet.Infrastructure.Features.Auth.Commands.AdminLogin;

public class AdminLoginCommandHandler : IRequestHandler<AdminLoginCommand, AuthResponseDto>
{
    private readonly FinVietDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _config;

    public AdminLoginCommandHandler(FinVietDbContext db, IJwtTokenService jwt, IConfiguration config)
    {
        _db = db; _jwt = jwt; _config = config;
    }

    public async Task<AuthResponseDto> Handle(AdminLoginCommand request, CancellationToken cancellationToken)
    {
        var admin = await _db.Admins
            .FirstOrDefaultAsync(a => a.Username == request.Username, cancellationToken);

        if (admin is null || !BCrypt.Net.BCrypt.Verify(request.Password, admin.PasswordHash))
            throw new UnauthorizedException("Invalid username or password.");

        var accessToken = _jwt.GenerateAccessToken(admin.AdminId, admin.Email, admin.Username, "Admin");
        var minutes     = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "15");

        return new AuthResponseDto
        {
            AccessToken       = accessToken,
            RefreshToken      = string.Empty,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(minutes),
            Profile = new ProfileDto
            {
                CustomerId      = admin.AdminId,
                FullName        = admin.Username,
                Email           = admin.Email,
                IsEmailVerified = true,
                IsActive        = true,
                CreatedAt       = admin.CreatedAt
            }
        };
    }
}