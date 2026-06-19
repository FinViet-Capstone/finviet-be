using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Persistence;

/// <summary>
/// Runs raw SQL migrations from the Migrations/ folder, then seeds default data.
/// Designed to be idempotent (uses IF NOT EXISTS / ON CONFLICT).
/// </summary>
public static class DbInitializer
{
    private static readonly string MigrationsFolder =
        Path.Combine(AppContext.BaseDirectory, "Persistence", "Migrations");

    public static async Task InitializeAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await ApplyMigrationsAsync(db, logger, cancellationToken);
        await SeedAsync(db, logger, cancellationToken);
    }

    private static async Task ApplyMigrationsAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(MigrationsFolder))
        {
            logger.LogWarning("Migrations folder not found at {Folder}. Skipping migrations.", MigrationsFolder);
            return;
        }

        var files = Directory.GetFiles(MigrationsFolder, "*.sql")
            .OrderBy(GetMigrationVersion)
            .ThenBy(Path.GetFileName)
            .ToArray();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var sql  = await File.ReadAllTextAsync(file, cancellationToken);

            if (string.IsNullOrWhiteSpace(sql))
                continue;

            try
            {
                logger.LogInformation("Applying migration {Name}...", name);
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                logger.LogInformation("Migration {Name} applied successfully.", name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Migration {Name} failed: {Message}", name, ex.Message);
                throw;
            }
        }
    }

    private static int GetMigrationVersion(string file)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName[0] != 'V')
        {
            return int.MaxValue;
        }

        var separatorIndex = fileName.IndexOf("__", StringComparison.Ordinal);
        var versionText = separatorIndex > 1
            ? fileName[1..separatorIndex]
            : fileName[1..];

        return int.TryParse(versionText, out var version)
            ? version
            : int.MaxValue;
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
                Status                = "ACTIVE",
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
