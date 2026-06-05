namespace FinViet.Application.Interfaces;

public interface IJwtTokenService
{
    /// <summary>Generates a short-lived JWT access token with role claim ("Customer" or "Admin").</summary>
    string GenerateAccessToken(Guid userId, string email, string fullName, string role);

    /// <summary>Generates a cryptographically random refresh token string.</summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates an access token and extracts the user id.
    /// Returns false if the token is invalid or expired.
    /// </summary>
    bool TryGetCustomerIdFromToken(string token, out Guid customerId);
}
