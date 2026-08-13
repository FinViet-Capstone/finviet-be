using DbUp;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinViet.Infrastructure.Persistence;

/// <summary>
/// Applies embedded DbUp migrations, validates the resulting PostgreSQL schema, and seeds
/// environment-appropriate application accounts. Schema migrations are the only source of DDL.
/// </summary>
public static class DbInitializer
{
    private const string MigrationResourceMarker = ".Persistence.Migrations.V";
    private const string BaselineReferenceScript = "FinViet.Infrastructure.Persistence.Migrations.V0002__baseline_reference_data.sql";
    private const int MinimumAdminPasswordLength = 12;

    public static async Task InitializeAsync(
        string connectionString,
        FinVietDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger,
        bool adoptBaseline = false,
        bool adoptionConfirmed = false,
        bool backupConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtext('finviet_database_initializer'));",
            lockConnection);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            if (adoptBaseline)
            {
                await AdoptExistingBaselineAsync(
                    connectionString,
                    adoptionConfirmed,
                    backupConfirmed,
                    logger,
                    cancellationToken);
            }

            RunMigrations(connectionString, logger);
            await ValidateSchemaAsync(lockConnection, cancellationToken);
            await SeedAsync(db, configuration, environment, logger, cancellationToken);
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtext('finviet_database_initializer'));",
                lockConnection);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static void RunMigrations(string connectionString, ILogger logger)
    {
        var upgrader = BuildUpgradeEngine(connectionString);
        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException("Database migration failed.", result.Error);

        foreach (var script in result.Scripts)
            logger.LogInformation("Applied database migration {ScriptName}.", script.Name);
    }

    private static DbUp.Engine.UpgradeEngine BuildUpgradeEngine(string connectionString) =>
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable("public", "schema_versions")
            .WithScriptsEmbeddedInAssembly(
                typeof(DbInitializer).Assembly,
                resourceName => resourceName.Contains(MigrationResourceMarker, StringComparison.Ordinal) &&
                                resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();

    private static async Task AdoptExistingBaselineAsync(
        string connectionString,
        bool adoptionConfirmed,
        bool backupConfirmed,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!adoptionConfirmed || !backupConfirmed)
        {
            throw new InvalidOperationException(
                "Baseline adoption requires both --confirm-adopt-baseline and --confirm-database-backup.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await VerifyAdoptionFingerprintAsync(connection, cancellationToken);

        var upgrader = BuildUpgradeEngine(connectionString);
        var result = upgrader.MarkAsExecuted(BaselineReferenceScript);
        if (!result.Successful)
            throw new InvalidOperationException("Could not journal the adopted database baseline.", result.Error);

        logger.LogInformation(
            "Adopted existing equivalent database schema through {BaselineScript}.",
            BaselineReferenceScript);
    }

    private static async Task VerifyAdoptionFingerprintAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                to_regclass('public.admins') IS NOT NULL
                AND to_regclass('public.customers') IS NOT NULL
                AND to_regclass('public.categories') IS NOT NULL
                AND to_regclass('public.transactions') IS NOT NULL
                AND to_regclass('public.wallets') IS NOT NULL
                AND to_regclass('public.customer_settings') IS NOT NULL
                AND to_regclass('public.ai_chat_sessions') IS NOT NULL
                AND to_regclass('public.ai_chat_messages') IS NOT NULL
                AND to_regclass('public.rag_document') IS NOT NULL
                AND to_regclass('public.rag_chunk') IS NOT NULL
                AND EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgcrypto')
                AND EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')
                AND (
                    SELECT format_type(attribute.atttypid, attribute.atttypmod)
                    FROM pg_attribute attribute
                    WHERE attribute.attrelid = 'public.rag_chunk'::regclass
                      AND attribute.attname = 'embedding'
                      AND NOT attribute.attisdropped
                ) = 'vector(768)'
                AND EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND indexname = 'ix_rag_chunk_embedding'
                      AND indexdef ILIKE '%USING hnsw%'
                      AND indexdef ILIKE '%vector_cosine_ops%'
                )
                AND (
                    SELECT array_agg(enum_value.enumlabel ORDER BY enum_value.enumsortorder)::text
                    FROM pg_type enum_type
                    JOIN pg_namespace enum_namespace ON enum_namespace.oid = enum_type.typnamespace
                    JOIN pg_enum enum_value ON enum_value.enumtypid = enum_type.oid
                    WHERE enum_namespace.nspname = 'public'
                      AND enum_type.typname = 'category_source'
                ) = '{persona,system}'
                AND (SELECT count(*) FROM public.buckets WHERE id IN ('needs', 'wants', 'savings')) = 3
                AND (SELECT count(*) FROM public.categories WHERE id LIKE 'cat_%') >= 18;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not true)
        {
            throw new InvalidOperationException(
                "The existing database does not match the reviewed FinViet baseline. " +
                "Restore the correct backup or reconcile schema drift before adoption.");
        }

        await EnsureNoJournalExistsAsync(connection, cancellationToken);
    }

    private static async Task EnsureNoJournalExistsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT to_regclass('public.schema_versions') IS NULL;";
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not true)
        {
            throw new InvalidOperationException(
                "Baseline adoption is only valid for an unjournaled restored database.");
        }
    }

    private static async Task ValidateSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                to_regclass('public.income_allocation_settings') IS NOT NULL
                AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'savings_goal_contributions'
                      AND column_name = 'type'
                      AND is_nullable = 'NO'
                )
                AND EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgcrypto')
                AND EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')
                AND (
                    SELECT format_type(attribute.atttypid, attribute.atttypmod)
                    FROM pg_attribute attribute
                    WHERE attribute.attrelid = 'public.rag_chunk'::regclass
                      AND attribute.attname = 'embedding'
                      AND NOT attribute.attisdropped
                ) = 'vector(768)'
                AND EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND indexname = 'ix_rag_chunk_embedding'
                      AND indexdef ILIKE '%USING hnsw%'
                      AND indexdef ILIKE '%vector_cosine_ops%'
                );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not true)
            throw new InvalidOperationException("Database migration completed but schema validation failed.");
    }

    private static async Task SeedAsync(
        FinVietDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await SeedAdminAsync(db, configuration, environment, logger, cancellationToken);

        if (environment.IsDevelopment() && configuration.GetValue("Database:SeedDemoData", true))
        {
            await SeedCustomersAsync(db, logger, cancellationToken);
            await SeedWalletsAndTransactionsAsync(db, logger, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAdminAsync(
        FinVietDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.Admins.AnyAsync(cancellationToken))
            return;

        var password = configuration["Admin:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(password) && environment.IsDevelopment())
            password = "Admin@123456";

        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumAdminPasswordLength)
        {
            throw new InvalidOperationException(
                $"Admin:DefaultPassword must be configured with at least {MinimumAdminPasswordLength} characters " +
                "when the database has no administrator.");
        }

        var username = configuration["Admin:DefaultUsername"]?.Trim();
        if (string.IsNullOrWhiteSpace(username))
            username = "admin";

        var email = configuration["Admin:DefaultEmail"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            email = "admin@finviet.local";

        db.Admins.Add(new Admin
        {
            AdminId = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        });

        logger.LogInformation("Seeded initial administrator {Username}.", username);
    }

    private static async Task SeedCustomersAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var seedCustomers = new[]
        {
            new { Email = "demo@finviet.local", FullName = "Demo User", Password = "Demo@1234", MonthlyIncomeExpected = 15_000_000m },
            new { Email = "alice@finviet.local", FullName = "Alice Nguyen", Password = "Alice@1234", MonthlyIncomeExpected = 20_000_000m },
            new { Email = "bob@finviet.local", FullName = "Bob Tran", Password = "Bob@12345", MonthlyIncomeExpected = 12_000_000m }
        };

        foreach (var seed in seedCustomers)
        {
            var email = seed.Email.ToLowerInvariant();
            if (await db.Customers.AnyAsync(customer => customer.Email == email, cancellationToken))
                continue;

            db.Customers.Add(new Customer
            {
                CustomerId = Guid.NewGuid(),
                FullName = seed.FullName,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(seed.Password),
                IsEmailVerified = true,
                EmailVerifiedAt = DateTime.UtcNow,
                IsActive = true,
                MonthlyIncomeExpected = seed.MonthlyIncomeExpected,
                CreatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Seeded development customer {Email}.", email);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedWalletsAndTransactionsAsync(
        FinVietDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var emails = new[] { "demo@finviet.local", "alice@finviet.local", "bob@finviet.local" };
        var customers = await db.Customers
            .Where(customer => emails.Contains(customer.Email))
            .ToListAsync(cancellationToken);

        var random = new Random(42);
        foreach (var customer in customers)
        {
            var existingWallet = await db.Wallets
                .FirstOrDefaultAsync(wallet => wallet.CustomerId == customer.CustomerId, cancellationToken);
            if (existingWallet is not null &&
                await db.Transactions.AnyAsync(
                    transaction => transaction.WalletId == existingWallet.WalletId,
                    cancellationToken))
            {
                continue;
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

            if (existingWallet is null)
                db.Wallets.Add(wallet);

            var startDate = DateTime.UtcNow.Date.AddDays(-60);
            var transactions = new List<Transaction>();
            decimal currentBalance = 10_000_000m;
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

            for (var day = 0; day <= 60; day++)
            {
                var currentDate = startDate.AddDays(day);
                if (currentDate.Day == 1)
                {
                    var salary = customer.MonthlyIncomeExpected ?? 15_000_000m;
                    transactions.Add(new Transaction
                    {
                        TransactionId = Guid.NewGuid(),
                        CustomerId = customer.CustomerId,
                        WalletId = wallet.WalletId,
                        CategoryId = "cat_salary",
                        Amount = salary,
                        TransactionType = "income",
                        Description = $"Lương tháng {currentDate.Month}",
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

                var transactionCount = customer.Email switch
                {
                    "demo@finviet.local" => random.Next(1, 4),
                    "alice@finviet.local" => random.Next(2, 6),
                    _ => random.Next(0, 3)
                };

                for (var index = 0; index < transactionCount; index++)
                {
                    var categoryId = SelectDevelopmentCategory(customer.Email, random);
                    var amount = SelectDevelopmentAmount(customer.Email, categoryId, random);
                    transactions.Add(CreateExpense(
                        customer.CustomerId,
                        wallet.WalletId,
                        categoryId,
                        amount,
                        currentDate.AddHours(random.Next(7, 23)),
                        random));
                    currentBalance -= amount;
                }
            }

            wallet.Balance = currentBalance;
            db.Transactions.AddRange(transactions);
            logger.LogInformation(
                "Seeded development wallet and {Count} transactions for {Email}.",
                transactions.Count,
                customer.Email);
        }
    }

    private static string SelectDevelopmentCategory(string email, Random random)
    {
        if (email == "demo@finviet.local")
        {
            var isNeed = random.NextDouble() < 0.6;
            var isWant = !isNeed && random.NextDouble() < 0.75;
            return isNeed
                ? (random.NextDouble() < 0.7 ? "cat_food" : "cat_transport")
                : (isWant ? "cat_dining" : "cat_savings");
        }

        if (email == "alice@finviet.local")
        {
            var isWant = random.NextDouble() < 0.7;
            return isWant
                ? (random.NextDouble() < 0.4
                    ? "cat_shopping"
                    : random.NextDouble() < 0.5 ? "cat_beauty" : "cat_dining")
                : (random.NextDouble() < 0.8 ? "cat_food" : "cat_transport");
        }

        return random.NextDouble() < 0.85
            ? (random.NextDouble() < 0.8 ? "cat_food" : "cat_housing")
            : "cat_invest";
    }

    private static decimal SelectDevelopmentAmount(string email, string categoryId, Random random)
    {
        if (email == "alice@finviet.local")
            return categoryId is "cat_shopping" or "cat_beauty" or "cat_dining"
                ? random.Next(200, 1500) * 1000m
                : random.Next(50, 300) * 1000m;

        if (email == "bob@finviet.local")
            return categoryId == "cat_invest"
                ? random.Next(1000, 3000) * 1000m
                : random.Next(20, 100) * 1000m;

        return categoryId switch
        {
            "cat_food" or "cat_transport" => random.Next(30, 200) * 1000m,
            "cat_dining" => random.Next(50, 300) * 1000m,
            _ => random.Next(500, 2000) * 1000m
        };
    }

    private static Transaction CreateExpense(
        Guid customerId,
        Guid walletId,
        string categoryId,
        decimal amount,
        DateTime date,
        Random random)
    {
        var merchants = new Dictionary<string, string[]>
        {
            ["cat_food"] = ["VinMart", "Bách Hóa Xanh", "CoopMart", "Chợ", "Circle K"],
            ["cat_transport"] = ["GrabBike", "Be", "Gojek", "Đổ xăng", "Taxi Mai Linh"],
            ["cat_housing"] = ["Điện lực EVN", "Tiền nước", "Tiền mạng VNPT", "Tiền rác"],
            ["cat_dining"] = ["Highlands Coffee", "The Coffee House", "Phúc Long", "Haidilao", "Golden Gate"],
            ["cat_shopping"] = ["Shopee", "Lazada", "Tiki", "Zara", "Uniqlo"],
            ["cat_beauty"] = ["Hasaki", "Guardian", "Hair Salon", "Spa"],
            ["cat_savings"] = ["Gửi tiết kiệm online", "Heo đất"],
            ["cat_invest"] = ["VNDirect", "TCBS", "Mua vàng"]
        };

        var merchant = merchants.TryGetValue(categoryId, out var categoryMerchants)
            ? categoryMerchants[random.Next(categoryMerchants.Length)]
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
