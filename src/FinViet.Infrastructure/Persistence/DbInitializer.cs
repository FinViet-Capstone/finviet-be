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
        // (merge origin/dev) Seed thư viện danh mục GLOBAL — BẮT BUỘC: mọi luồng transaction/
        // budget/goal phụ thuộc category; thiếu là app vỡ. Idempotent theo CategoryId.
        await SeedCategoriesAsync(db, logger, cancellationToken);
        // (merge origin/dev) Demo customers (demo/alice/bob) cho môi trường dev/demo. Idempotent
        // theo email. Local trước đây cố ý KHÔNG seed (tránh demo account vào mọi env); bật lại để
        // khớp origin/dev và dữ liệu dev hiện có. Tắt dòng này nếu deploy production.
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

    /// <summary>
    /// (Merge của origin/dev `SeedCategoriesAsync`, đã chỉnh cho entity bản LOCAL.)
    /// Khác biệt khi gộp: origin/dev dùng <c>Type = CategoryType.Expense</c> + <c>ExpenseClass</c>;
    /// local dùng <c>Type</c> là string (qua PgEnumStringConverter&lt;CategoryType&gt; → pg enum
    /// <c>category_type</c>) và field <c>DefaultBucket</c> (varchar, FK buckets: needs/wants/savings).
    /// Vì vậy mọi <c>CategoryType.Expense</c> → <c>"expense"</c>, <c>CategoryType.Income</c> → <c>"income"</c>,
    /// <c>ExpenseClass</c> → <c>DefaultBucket</c>. Income có <c>DefaultBucket = null</c>.
    /// </summary>
    private static async Task SeedCategoriesAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        var seedCategories = new[]
        {
            new Category { CategoryId = "cat_food",              CategoryName = "Ăn uống",              NameEn = "Food",                Type = "expense", Icon = "restaurant",      Color = "#4EDEA3", DefaultBucket = "needs",   SortOrder = 1 },
            new Category { CategoryId = "cat_housing",           CategoryName = "Nhà ở & Tiện ích",     NameEn = "Housing & Utilities", Type = "expense", Icon = "home",            Color = "#D0BCFF", DefaultBucket = "needs",   SortOrder = 2 },
            new Category { CategoryId = "cat_transport",         CategoryName = "Di chuyển",            NameEn = "Transport",           Type = "expense", Icon = "directions_car",  Color = "#90CAF9", DefaultBucket = "needs",   SortOrder = 3 },
            new Category { CategoryId = "cat_health",            CategoryName = "Sức khỏe & Y tế",      NameEn = "Health",              Type = "expense", Icon = "local_hospital",  Color = "#EF9A9A", DefaultBucket = "needs",   SortOrder = 4 },
            new Category { CategoryId = "cat_education",         CategoryName = "Giáo dục",             NameEn = "Education",           Type = "expense", Icon = "school",          Color = "#FFE082", DefaultBucket = "needs",   SortOrder = 5 },
            new Category { CategoryId = "cat_family",            CategoryName = "Gửi tiền gia đình",    NameEn = "Family support",      Type = "expense", Icon = "family_restroom", Color = "#BCAAA4", DefaultBucket = "needs",   SortOrder = 6 },
            new Category { CategoryId = "cat_entertain",         CategoryName = "Giải trí",             NameEn = "Entertainment",       Type = "expense", Icon = "sports_esports",  Color = "#CE93D8", DefaultBucket = "wants",   SortOrder = 7 },
            new Category { CategoryId = "cat_beauty",            CategoryName = "Quần áo & Thời trang", NameEn = "Fashion",             Type = "expense", Icon = "checkroom",       Color = "#F8BBD0", DefaultBucket = "wants",   SortOrder = 8 },
            new Category { CategoryId = "cat_shopping",          CategoryName = "Mua sắm online",       NameEn = "Shopping",            Type = "expense", Icon = "shopping_bag",    Color = "#FFB690", DefaultBucket = "wants",   SortOrder = 9 },
            new Category { CategoryId = "cat_dining",            CategoryName = "Ăn ngoài & Cà phê",    NameEn = "Dining & Coffee",     Type = "expense", Icon = "local_cafe",      Color = "#FFCC80", DefaultBucket = "wants",   SortOrder = 10 },
            new Category { CategoryId = "cat_savings",           CategoryName = "Tiết kiệm",            NameEn = "Savings",             Type = "expense", Icon = "savings",         Color = "#4EDEA3", DefaultBucket = "savings", SortOrder = 11 },
            new Category { CategoryId = "cat_invest",            CategoryName = "Đầu tư",               NameEn = "Investment",          Type = "expense", Icon = "trending_up",     Color = "#A5D6A7", DefaultBucket = "savings", SortOrder = 12 },
            new Category { CategoryId = "cat_savings_goal",      CategoryName = "Mục tiêu tiết kiệm",   NameEn = "Saving goal",         Type = "expense", Icon = "flag",            Color = "#80CBC4", DefaultBucket = "savings", SortOrder = 13 },
            new Category { CategoryId = "cat_salary",            CategoryName = "Lương",                NameEn = "Salary",              Type = "income",  Icon = "payments",        Color = "#81C784", DefaultBucket = null,      SortOrder = 101 },
            new Category { CategoryId = "cat_freelance",         CategoryName = "Freelance",            NameEn = "Freelance",           Type = "income",  Icon = "work",            Color = "#64B5F6", DefaultBucket = null,      SortOrder = 102 },
            new Category { CategoryId = "cat_investment_return", CategoryName = "Lợi nhuận đầu tư",     NameEn = "Investment return",   Type = "income",  Icon = "trending_up",     Color = "#A5D6A7", DefaultBucket = null,      SortOrder = 103 },
            new Category { CategoryId = "cat_gift",              CategoryName = "Quà tặng",             NameEn = "Gift",                Type = "income",  Icon = "redeem",          Color = "#F48FB1", DefaultBucket = null,      SortOrder = 104 },
            new Category { CategoryId = "cat_income_other",      CategoryName = "Thu nhập khác",        NameEn = "Other income",        Type = "income",  Icon = "more_horiz",      Color = "#B0BEC5", DefaultBucket = null,      SortOrder = 105 },
        };

        var ids = seedCategories.Select(c => c.CategoryId).ToArray();
        var existingIds = await db.Categories
            .Where(c => ids.Contains(c.CategoryId))
            .Select(c => c.CategoryId)
            .ToListAsync(ct);

        var added = 0;
        foreach (var category in seedCategories)
        {
            if (existingIds.Contains(category.CategoryId)) continue;
            db.Categories.Add(category);
            added++;
        }

        if (added > 0)
            logger.LogInformation("Seeded {Count} categories", added);
    }

    /// <summary>
    /// (Merge của origin/dev `SeedCustomersAsync`.) Customer entity giống nhau ở cả hai bản nên
    /// bê nguyên; idempotent theo email. NeedsPct/WantsPct/SavingsPct lấy default 50/30/20 của entity.
    /// </summary>
    private static async Task SeedCustomersAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        var seedCustomers = new[]
        {
            new { Email = "demo@finviet.local",  FullName = "Demo User",    Password = "Demo@1234",  MonthlyIncomeExpected = 15_000_000m },
            new { Email = "alice@finviet.local", FullName = "Alice Nguyen", Password = "Alice@1234", MonthlyIncomeExpected = 20_000_000m },
            new { Email = "bob@finviet.local",   FullName = "Bob Tran",     Password = "Bob@12345",  MonthlyIncomeExpected = 12_000_000m }
        };

        foreach (var s in seedCustomers)
        {
            var email = s.Email.ToLowerInvariant();
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

            logger.LogInformation("Seeded customer: {Email}", email);
        }
    }
}
