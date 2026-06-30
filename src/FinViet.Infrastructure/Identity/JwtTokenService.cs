using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FinViet.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config) => _config = config;

    public string GenerateAccessToken(Guid userId, string email, string fullName, string role)
    {
        var secret  = _config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured.");
        var issuer  = _config["Jwt:Issuer"]   ?? "FinViet";
        var audience= _config["Jwt:Audience"] ?? "FinViet";
        var expiry  = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "15");

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Name,  fullName),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role,               role),
            new Claim("customerId",                  userId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(expiry),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    public bool TryGetCustomerIdFromToken(string token, out Guid customerId)
    {
        customerId = Guid.Empty;
        try
        {
            var secret   = _config["Jwt:Secret"] ?? string.Empty;
            var handler  = new JwtSecurityTokenHandler();
            var key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = key,
                ValidateIssuer          = false,
                ValidateAudience        = false,
                ClockSkew               = TimeSpan.Zero
            };

            var principal = handler.ValidateToken(token, parameters, out _);
            var claim     = principal.FindFirst("customerId")
                         ?? principal.FindFirst(JwtRegisteredClaimNames.Sub);

            if (claim is null) return false;

            return Guid.TryParse(claim.Value, out customerId);
        }
        catch
        {
            return false;
        }
    }
}
