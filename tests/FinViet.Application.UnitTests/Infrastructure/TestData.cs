using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Application.UnitTests.Infrastructure;

internal static class TestData
{
    internal static Customer Customer(
        string email = "customer@example.com",
        string password = "Password1",
        bool isEmailVerified = true,
        bool isActive = true)
        => new()
        {
            CustomerId = Guid.NewGuid(), FullName = "Test Customer", Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsEmailVerified = isEmailVerified, IsActive = isActive, CreatedAt = DateTime.UtcNow
        };

    internal static EmailVerificationToken EmailToken(
        Customer customer, string token, EmailTokenType tokenType,
        DateTime? expiresAt = null, DateTime? usedAt = null)
        => new()
        {
            TokenId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer,
            Token = token, TokenType = tokenType, ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
            UsedAt = usedAt, CreatedAt = DateTime.UtcNow
        };

    internal static RefreshToken RefreshToken(
        Customer customer, string token = "current-refresh-token",
        bool isRevoked = false, DateTime? expiresAt = null)
        => new()
        {
            TokenId = Guid.NewGuid(), CustomerId = customer.CustomerId, Customer = customer,
            Token = token, IsRevoked = isRevoked,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow
        };
}
