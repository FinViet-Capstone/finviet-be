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
        await EnsureAdditiveTablesAsync(db, logger, cancellationToken);
        await SeedAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Idempotently creates additive tables that are NOT part of the externally-provisioned
    /// v3 schema. The v3 path skips all SQL migrations (see <see cref="ApplyMigrationsAsync"/>),
    /// so new tables introduced after v3 must be created here to exist on every environment.
    /// </summary>
    private static async Task EnsureAdditiveTablesAsync(
        FinVietDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS merchant_rules (
    rule_id          uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id      uuid         NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    merchant_keyword varchar(255) NOT NULL,
    category_id      varchar(40)  NOT NULL REFERENCES categories(id),
    applied_count    integer      NOT NULL DEFAULT 0,
    created_at       timestamptz  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_merchant_rules_customer_keyword
    ON merchant_rules (customer_id, lower(merchant_keyword));
CREATE INDEX IF NOT EXISTS ix_merchant_rules_customer
    ON merchant_rules (customer_id);

-- ── RAG layer (pgvector) ────────────────────────────────────────────────────
-- Redundant AI tables removed: re-process is now a query over unclassified
-- transactions, rate limiting moved in-memory, and per-user bucket overrides are
-- superseded by customer_categories. Drops are idempotent (may not exist on v3).
DROP TABLE IF EXISTS ai_classification_queue;
DROP TABLE IF EXISTS ai_usage_log;
DROP TABLE IF EXISTS user_category_buckets;

-- Category-request admin-approval flow removed: customers now reassign a category's
-- bucket directly (PUT /api/categories/:id/bucket), writing straight to
-- customer_categories with no admin review step needed.
DROP TABLE IF EXISTS category_requests;
DROP TYPE IF EXISTS category_request_status;

-- pgvector extension must be installed on the Postgres server image.
CREATE EXTENSION IF NOT EXISTS vector;

-- A document is either a global knowledge source (customer_id NULL, e.g. an
-- ingested finance PDF) or a per-customer narrative (weekly_report/monthly_summary).
CREATE TABLE IF NOT EXISTS rag_document (
    id          uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id uuid         NULL REFERENCES customers(id) ON DELETE CASCADE,
    source_type varchar(20)  NOT NULL,
    title       varchar(255) NOT NULL,
    uri         varchar(512) NULL,
    created_at  timestamptz  NOT NULL DEFAULT now()
);

-- The configured embedding model must produce 768-dimensional vectors.
CREATE TABLE IF NOT EXISTS rag_chunk (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid        NOT NULL REFERENCES rag_document(id) ON DELETE CASCADE,
    customer_id uuid        NULL,
    content     text        NOT NULL,
    embedding   vector(768) NOT NULL,
    metadata    jsonb       NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_rag_chunk_customer ON rag_chunk (customer_id);
CREATE INDEX IF NOT EXISTS ix_rag_chunk_embedding
    ON rag_chunk USING hnsw (embedding vector_cosine_ops);";

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("Ensured additive tables (merchant_rules, rag_document, rag_chunk), dropped redundant AI tables, and dropped category_requests.");
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

        // The externally-provisioned v3 database is maintained by explicit SQL scripts.
        // In particular, PostgreSQL enum additions must be applied before the API starts
        // and before Npgsql initializes its enum mappings. Never replay those scripts here.
        if (await IsV3SchemaAppliedAsync(db, cancellationToken))
        {
            logger.LogInformation(
                "v3 schema detected. Skipping startup SQL migrations; apply v3 migration scripts before starting the API.");
            return;
        }

        // Once the v2.1 rename (V11) has run, the singular `category`/`transaction`
        // tables no longer exist, so migrations V2–V10 (which ALTER them) would fail
        // on every subsequent startup. Skip everything below the rename in that case.
        else if (await IsCategoryTransactionV21AppliedAsync(db, cancellationToken))
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
        await SeedWalletsAndTransactionsAsync(db, logger, cancellationToken);
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
    
    private static async Task SeedWalletsAndTransactionsAsync(FinVietDbContext db, ILogger logger, CancellationToken ct)
    {
        var emails = new[] { "demo@finviet.local", "alice@finviet.local", "bob@finviet.local" };
        var customers = await db.Customers
            .Where(c => emails.Contains(c.Email))
            .ToListAsync(ct);

        if (!customers.Any()) return;

        var rand = new Random(42); // fixed seed for reproducible data

        foreach (var customer in customers)
        {
            // Check if user already has a wallet
            var existingWallet = await db.Wallets.FirstOrDefaultAsync(w => w.CustomerId == customer.CustomerId, ct);
            if (existingWallet != null && await db.Transactions.AnyAsync(t => t.WalletId == existingWallet.WalletId, ct))
            {
                continue; // Already seeded
            }

            var wallet = existingWallet ?? new Wallet
            {
                WalletId = Guid.NewGuid(),
                CustomerId = customer.CustomerId,
                WalletName = "Ví Chính",
                WalletType = "basic",
                Balance = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (existingWallet == null)
            {
                db.Wallets.Add(wallet);
            }

            // Generate 60 days of transactions
            var startDate = DateTime.UtcNow.Date.AddDays(-60);
            var transactions = new List<Transaction>();
            decimal currentBalance = 10_000_000m; // Initial balance
            
            // Generate some initial balance transaction
            transactions.Add(new Transaction
            {
                TransactionId = Guid.NewGuid(),
                CustomerId = customer.CustomerId,
                WalletId = wallet.WalletId,
                CategoryId = "cat_salary",
                Amount = currentBalance,
                TransactionType = "income",
                Description = "Số dư ban đầu",
                TransactionDate = startDate.AddDays(-1),
                EntryMethod = "manual",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsAiClassified = true,
                AiConfidence = 0.99m,
                AiCategoryGuess = "cat_salary"
            });

            for (int i = 0; i <= 60; i++)
            {
                var currentDate = startDate.AddDays(i);
                
                // Add salary on the 1st of each month
                if (currentDate.Day == 1)
                {
                    decimal salary = customer.MonthlyIncomeExpected ?? 15_000_000m;
                    transactions.Add(new Transaction
                    {
                        TransactionId = Guid.NewGuid(),
                        CustomerId = customer.CustomerId,
                        WalletId = wallet.WalletId,
                        CategoryId = "cat_salary",
                        Amount = salary,
                        TransactionType = "income",
                        Description = "Lương tháng " + currentDate.Month,
                        Merchant = "Công ty TNHH ABC",
                        TransactionDate = currentDate.AddHours(9),
                        EntryMethod = "manual",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsAiClassified = true,
                        AiConfidence = 0.95m,
                        AiCategoryGuess = "cat_salary"
                    });
                    currentBalance += salary;
                }

                int numTxPerDay = 0;
                
                if (customer.Email == "demo@finviet.local")
                {
                    // Balanced spending (1-3 tx per day)
                    numTxPerDay = rand.Next(1, 4);
                    for (int j = 0; j < numTxPerDay; j++)
                    {
                        var isNeed = rand.NextDouble() < 0.6; // 60% needs, 30% wants, 10% savings
                        var isWant = !isNeed && rand.NextDouble() < 0.75;
                        
                        string cat = isNeed ? (rand.NextDouble() < 0.7 ? "cat_food" : "cat_transport") 
                            : (isWant ? "cat_dining" : "cat_savings");
                        
                        decimal amount = isNeed ? rand.Next(30, 200) * 1000m : (isWant ? rand.Next(50, 300) * 1000m : rand.Next(500, 2000) * 1000m);
                        
                        transactions.Add(CreateExpense(customer.CustomerId, wallet.WalletId, cat, amount, currentDate.AddHours(rand.Next(8, 22)), rand));
                        currentBalance -= amount;
                    }
                }
                else if (customer.Email == "alice@finviet.local")
                {
                    // High spending, lots of wants (2-5 tx per day)
                    numTxPerDay = rand.Next(2, 6);
                    for (int j = 0; j < numTxPerDay; j++)
                    {
                        var isWant = rand.NextDouble() < 0.7; // 70% wants
                        
                        string cat = isWant ? 
                            (rand.NextDouble() < 0.4 ? "cat_shopping" : (rand.NextDouble() < 0.5 ? "cat_beauty" : "cat_dining")) : 
                            (rand.NextDouble() < 0.8 ? "cat_food" : "cat_transport");
                            
                        decimal amount = isWant ? rand.Next(200, 1500) * 1000m : rand.Next(50, 300) * 1000m;
                        
                        transactions.Add(CreateExpense(customer.CustomerId, wallet.WalletId, cat, amount, currentDate.AddHours(rand.Next(9, 23)), rand));
                        currentBalance -= amount;
                    }
                }
                else if (customer.Email == "bob@finviet.local")
                {
                    // Frugal (0-2 tx per day, mostly needs)
                    numTxPerDay = rand.Next(0, 3);
                    for (int j = 0; j < numTxPerDay; j++)
                    {
                        var isNeed = rand.NextDouble() < 0.85; // 85% needs
                        
                        string cat = isNeed ? (rand.NextDouble() < 0.8 ? "cat_food" : "cat_housing") : "cat_invest";
                        
                        decimal amount = isNeed ? rand.Next(20, 100) * 1000m : rand.Next(1000, 3000) * 1000m;
                        
                        transactions.Add(CreateExpense(customer.CustomerId, wallet.WalletId, cat, amount, currentDate.AddHours(rand.Next(7, 20)), rand));
                        currentBalance -= amount;
                    }
                }
            }

            wallet.Balance = currentBalance;
            db.Transactions.AddRange(transactions);
            logger.LogInformation("Seeded wallet and {Count} transactions for customer {Email}", transactions.Count, customer.Email);
        }
    }

    private static Transaction CreateExpense(Guid customerId, Guid walletId, string categoryId, decimal amount, DateTime date, Random rand)
    {
        var merchants = new System.Collections.Generic.Dictionary<string, string[]>
        {
            { "cat_food", new[] { "VinMart", "Bách Hóa Xanh", "CoopMart", "Chợ", "Circle K" } },
            { "cat_transport", new[] { "GrabBike", "Be", "Gojek", "Đổ xăng", "Taxi Mai Linh" } },
            { "cat_housing", new[] { "Điện lực EVN", "Tiền nước", "Tiền mạng VNPT", "Tiền rác" } },
            { "cat_dining", new[] { "Highlands Coffee", "The Coffee House", "Phúc Long", "Haidilao", "Golden Gate" } },
            { "cat_shopping", new[] { "Shopee", "Lazada", "Tiki", "Zara", "Uniqlo" } },
            { "cat_beauty", new[] { "Hasaki", "Guardian", "Hair Salon", "Spa" } },
            { "cat_savings", new[] { "Gửi tiết kiệm online", "Heo đất" } },
            { "cat_invest", new[] { "VNDirect", "TCBS", "Mua vàng" } }
        };

        string merchant = merchants.ContainsKey(categoryId) 
            ? merchants[categoryId][rand.Next(merchants[categoryId].Length)] 
            : "Chi tiêu";

        return new Transaction
        {
            TransactionId = Guid.NewGuid(),
            CustomerId = customerId,
            WalletId = walletId,
            CategoryId = categoryId,
            Amount = amount,
            TransactionType = "expense",
            Description = merchant,
            Merchant = merchant,
            TransactionDate = date,
            EntryMethod = "manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsAiClassified = true,
            AiConfidence = 0.9m,
            AiCategoryGuess = categoryId
        };
    }
}
