using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Persistence;

/// <summary>
/// Seeds default data (admin + demo customers) into an already-provisioned database.
/// The database schema is the source of truth and is created externally from
/// <c>db/schema_v2.1.sql</c>; this initializer NO LONGER runs SQL migrations.
/// Seeding goes through EF entities, so it stays in sync with the mapped schema.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedAsync(db, logger, cancellationToken);
    }

    private static async Task SeedAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await SeedAdminAsync(db, logger, cancellationToken);
        await SeedCustomersAsync(db, logger, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAdminAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        const string adminUsername = "admin";
        var existing = await db.Admins.FirstOrDefaultAsync(a => a.Username == adminUsername, ct);
        if (existing is not null) return;

        db.Admins.Add(new Admin
        {
            AdminId      = Guid.NewGuid(),
            Username     = adminUsername,
            Email        = "admin@finviet.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            CreatedAt    = DateTime.UtcNow
        });

        logger.LogInformation("Seeded admin account: username={Username} password=Admin@123", adminUsername);
    }

    private static async Task SeedCustomersAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        var seedCustomers = new[]
        {
            new
            {
                Email                 = "demo@finviet.local",
                FullName              = "Demo User",
                Password              = "Demo@1234",
                MonthlyIncomeExpected = 15_000_000m
            },
            new
            {
                Email                 = "alice@finviet.local",
                FullName              = "Alice Nguyen",
                Password              = "Alice@1234",
                MonthlyIncomeExpected = 20_000_000m
            },
            new
            {
                Email                 = "bob@finviet.local",
                FullName              = "Bob Tran",
                Password              = "Bob@12345",
                MonthlyIncomeExpected = 12_000_000m
            }
        };

        foreach (var s in seedCustomers)
        {
            var email = s.Email.ToLower();
            var exists = await db.Customers.AnyAsync(c => c.Email == email, ct);
            if (exists) continue;

            db.Customers.Add(new Customer
            {
                CustomerId            = Guid.NewGuid(),
                FullName              = s.FullName,
                Email                 = email,
                PasswordHash          = BCrypt.Net.BCrypt.HashPassword(s.Password),
                IsEmailVerified       = true,
                EmailVerifiedAt       = DateTime.UtcNow,
                IsActive              = true,
                MonthlyIncomeExpected = s.MonthlyIncomeExpected,
                CreatedAt             = DateTime.UtcNow
            });

            logger.LogInformation("Seeded customer: {Email} password={Password}", email, s.Password);
        }
    }
}
