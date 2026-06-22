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

        // The v3 schema (Finviet_update) is provisioned externally with plural tables
        // (customers, wallets, ...) and Postgres enum columns. None of the V2–V13 SQL
        // migrations apply — they transform the legacy singular schema toward v2.1 and
        // would fail against v3 (enum vs text casts, customer(customer_id) FKs, ...).
        // Detect v3 by the plural `customers` table and skip all SQL migrations; seeding
        // below is idempotent and still runs.
        if (await IsV3SchemaAppliedAsync(db, cancellationToken))
        {
            logger.LogInformation("v3 schema detected (public.customers exists). Skipping SQL migrations.");
            return;
        }

        var files = Directory.GetFiles(MigrationsFolder, "*.sql")
            .OrderBy(GetMigrationVersion)
            .ThenBy(Path.GetFileName)
            .ToArray();

        // Once the v2.1 rename (V11) has run, the singular `category`/`transaction`
        // tables no longer exist, so migrations V2–V10 (which ALTER them) would fail
        // on every subsequent startup. Skip everything below the rename in that case.
        if (await IsCategoryTransactionV21AppliedAsync(db, cancellationToken))
        {
            files = files
                .Where(file => GetMigrationVersion(file) >= 11)
                .ToArray();
        }

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

    /// <summary>
    /// True once the schema has been migrated to v2.1, i.e. the plural `categories`
    /// table exists and the legacy singular `category` table has been renamed away.
    /// </summary>
    private static async Task<bool> IsCategoryTransactionV21AppliedAsync(
        FinVietDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;

        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT to_regclass('public.categories') IS NOT NULL AND to_regclass('public.category') IS NULL";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool applied && applied;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// True when the database is on the v3 schema, identified by the plural
    /// <c>public.customers</c> table. The v2.1 schema still used singular <c>customer</c>,
    /// so this only matches the externally-provisioned v3 database.
    /// </summary>
    private static async Task<bool> IsV3SchemaAppliedAsync(
        FinVietDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;

        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('public.customers') IS NOT NULL";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool applied && applied;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task SeedAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await SeedAdminAsync(db, logger, cancellationToken);
        // Demo customers (demo/alice/bob) are intentionally NOT seeded — they wrote demo
        // accounts into every environment on startup. Re-enable only for local fixtures.
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
}
