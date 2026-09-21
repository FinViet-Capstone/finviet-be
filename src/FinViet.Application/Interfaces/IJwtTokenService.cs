namespace FinViet.Application.Interfaces;

public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT access token with role claim ("Customer" or "Admin").
    /// When <paramref name="expiryMinutes"/> is null the default from Jwt:AccessTokenExpiryMinutes is used.
    /// </summary>
    string GenerateAccessToken(Guid userId, string email, string fullName, string role, int? expiryMinutes = null);

    /// <summary>Generates a cryptographically random refresh token string.</summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates an access token and extracts the user id.
    /// Returns false if the token is invalid or expired.
    /// </summary>
    bool TryGetCustomerIdFromToken(string token, out Guid customerId);
}
