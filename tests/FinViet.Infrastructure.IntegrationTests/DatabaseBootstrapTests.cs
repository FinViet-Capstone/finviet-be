using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FinViet.Infrastructure.IntegrationTests;

public sealed class DatabaseBootstrapTests
{
    [SkippableFact]
    public async Task InitializeAsync_FreshDatabase_IsCompleteAndRepeatable()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        Skip.If(database is null, DisposableDatabase.SkipReason);

        await using (var context = CreateDbContext(database!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "IntegrationOnly!2026"),
                new TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.buckets;"));
        Assert.Equal(18, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.categories;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
        Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.customers;"));
        Assert.Equal("vector(768)", await ScalarAsync<string>(connection, """
            SELECT format_type(attribute.atttypid, attribute.atttypmod)
            FROM pg_attribute attribute
            WHERE attribute.attrelid = 'public.rag_chunk'::regclass
              AND attribute.attname = 'embedding';
            """));

        await using (var context = CreateDbContext(database.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "DifferentIgnored!2026"),
                new TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(18, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.categories;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
    }

    [SkippableFact]
    public async Task InitializeAsync_ProductionWithoutAdminPassword_FailsClosed()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        Skip.If(database is null, DisposableDatabase.SkipReason);

        await using var context = CreateDbContext(database!.ConnectionString);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false),
                new TestHostEnvironment(Environments.Production),
                NullLogger.Instance));

        Assert.Contains("Admin:DefaultPassword", exception.Message, StringComparison.Ordinal);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
    }

    [SkippableFact]
    public async Task InitializeAsync_DevelopmentDemoGate_ControlsBusinessSeeds()
    {
        await using var disabledDatabase = await DisposableDatabase.CreateAsync();
        Skip.If(disabledDatabase is null, DisposableDatabase.SkipReason);

        await using (var context = CreateDbContext(disabledDatabase!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                disabledDatabase.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false),
                new TestHostEnvironment(Environments.Development),
                NullLogger.Instance);
        }

        await using (var connection = new NpgsqlConnection(disabledDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.customers;"));
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.wallets;"));
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.transactions;"));
        }

        await using var enabledDatabase = await DisposableDatabase.CreateAsync();
        Skip.If(enabledDatabase is null, DisposableDatabase.SkipReason);

        await using (var context = CreateDbContext(enabledDatabase!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                enabledDatabase.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: true),
                new TestHostEnvironment(Environments.Development),
                NullLogger.Instance);
        }

        await using (var connection = new NpgsqlConnection(enabledDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.customers;"));
            Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.wallets;"));
            Assert.True(await ScalarAsync<long>(connection, "SELECT count(*) FROM public.transactions;") > 3);
        }
    }

    [SkippableFact]
    public async Task InitializeAsync_ConcurrentInitializers_SerializeAndSeedOnce()
    {
        await using var database = await DisposableDatabase.CreateAsync();
        Skip.If(database is null, DisposableDatabase.SkipReason);

        async Task InitializeAsync()
        {
            await using var context = CreateDbContext(database!.ConnectionString);
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "IntegrationOnly!2026"),
                new TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        await Task.WhenAll(InitializeAsync(), InitializeAsync());

        await using var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
        Assert.Equal(18, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.categories;"));
    }

    private static FinVietDbContext CreateDbContext(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<EmailTokenType>("email_token_type");
        dataSourceBuilder.MapEnum<Gender>("gender");
        dataSourceBuilder.MapEnum<AppLanguage>("app_language");
        dataSourceBuilder.MapEnum<AppTheme>("app_theme");
        dataSourceBuilder.MapEnum<WalletType>("wallet_type");
        dataSourceBuilder.MapEnum<TransactionType>("transaction_type");
        dataSourceBuilder.MapEnum<EntryMethod>("entry_method");
        dataSourceBuilder.MapEnum<CategoryType>("category_type");
        dataSourceBuilder.MapEnum<CategorySource>("category_source");
        dataSourceBuilder.MapEnum<NotificationType>("notification_type");
        dataSourceBuilder.MapEnum<NotificationEntityType>("notification_entity_type");
        dataSourceBuilder.MapEnum<SubscriptionStatus>("subscription_status");
        dataSourceBuilder.MapEnum<ChatRole>("chat_role");
        dataSourceBuilder.MapEnum<ScoreView>("score_view");
        dataSourceBuilder.MapEnum<ScoreColor>("score_color");
        dataSourceBuilder.EnableUnmappedTypes();
        dataSourceBuilder.UseVector();

        var options = new DbContextOptionsBuilder<FinVietDbContext>()
            .UseNpgsql(dataSourceBuilder.Build(), options => options.UseVector())
            .Options;
        return new FinVietDbContext(options);
    }

    private static IConfiguration BuildConfiguration(bool seedDemoData, string? adminPassword = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:SeedDemoData"] = seedDemoData.ToString()
        };

        if (adminPassword is not null)
            values["Admin:DefaultPassword"] = adminPassword;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FinViet.Infrastructure.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DisposableDatabase : IAsyncDisposable
    {
        public const string SkipReason =
            "Set FINVIET_TEST_ADMIN_CONNECTION to a PostgreSQL maintenance connection that can create databases and extensions.";

        private readonly string _adminConnectionString;
        private readonly string _databaseName;

        private DisposableDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            _adminConnectionString = adminConnectionString;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<DisposableDatabase?> CreateAsync()
        {
            var adminConnectionString = Environment.GetEnvironmentVariable("FINVIET_TEST_ADMIN_CONNECTION");
            if (string.IsNullOrWhiteSpace(adminConnectionString))
                return null;

            var databaseName = $"finviet_bootstrap_test_{Guid.NewGuid():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString);
            var maintenanceDatabase = string.IsNullOrWhiteSpace(adminBuilder.Database)
                ? "postgres"
                : adminBuilder.Database;
            adminBuilder.Database = maintenanceDatabase;

            await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
            try
            {
                await adminConnection.OpenAsync();
                await ExecuteAsync(adminConnection, $"CREATE DATABASE {QuoteIdentifier(databaseName)};");

                var databaseBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
                {
                    Database = databaseName
                };
                await using var databaseConnection = new NpgsqlConnection(databaseBuilder.ConnectionString);
                await databaseConnection.OpenAsync();
                await ExecuteAsync(databaseConnection, "CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;");
                await ExecuteAsync(databaseConnection, "CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;");

                return new DisposableDatabase(
                    adminBuilder.ConnectionString,
                    databaseName,
                    databaseBuilder.ConnectionString);
            }
            catch
            {
                if (adminConnection.State == System.Data.ConnectionState.Open)
                {
                    await ExecuteAsync(
                        adminConnection,
                        $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);");
                }

                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                $"DROP DATABASE IF EXISTS {QuoteIdentifier(_databaseName)} WITH (FORCE);");
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string identifier) =>
            $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
