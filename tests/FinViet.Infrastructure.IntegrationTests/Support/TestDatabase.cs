using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace FinViet.Infrastructure.IntegrationTests.Support;

/// <summary>
/// Shared plumbing for tests that need a real, disposable PostgreSQL database - things the
/// InMemory EF provider can't exercise (DbUp migrations, raw SQL, row locks). Self-skips via
/// <see cref="DisposableDatabase.CreateAsync"/> when FINVIET_TEST_ADMIN_CONNECTION isn't set.
/// </summary>
internal static class TestDatabase
{
    internal static FinVietDbContext CreateDbContext(string connectionString)
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

    internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FinViet.Infrastructure.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal sealed class DisposableDatabase : IAsyncDisposable
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

        public static async Task<DisposableDatabase?> CreateAsync(string namePrefix = "finviet_test")
        {
            var adminConnectionString = Environment.GetEnvironmentVariable("FINVIET_TEST_ADMIN_CONNECTION");
            if (string.IsNullOrWhiteSpace(adminConnectionString))
                return null;

            var databaseName = $"{namePrefix}_{Guid.NewGuid():N}";
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
