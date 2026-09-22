using FinViet.Infrastructure.IntegrationTests.Support;
using FinViet.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FinViet.Infrastructure.IntegrationTests;

public sealed class DatabaseBootstrapTests
{
    [SkippableFact]
    public async Task InitializeAsync_FreshDatabase_IsCompleteAndRepeatable()
    {
        await using var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_bootstrap_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);

        await using (var context = TestDatabase.CreateDbContext(database!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "IntegrationOnly!2026"),
                new TestDatabase.TestHostEnvironment(Environments.Production),
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

        await using (var context = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "DifferentIgnored!2026"),
                new TestDatabase.TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(18, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.categories;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
    }

    [SkippableFact]
    public async Task InitializeAsync_ProductionWithoutAdminPassword_FailsClosed()
    {
        await using var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_bootstrap_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);

        await using var context = TestDatabase.CreateDbContext(database!.ConnectionString);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false),
                new TestDatabase.TestHostEnvironment(Environments.Production),
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
        await using var disabledDatabase = await TestDatabase.DisposableDatabase.CreateAsync("finviet_bootstrap_test");
        Skip.If(disabledDatabase is null, TestDatabase.DisposableDatabase.SkipReason);

        await using (var context = TestDatabase.CreateDbContext(disabledDatabase!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                disabledDatabase.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false),
                new TestDatabase.TestHostEnvironment(Environments.Development),
                NullLogger.Instance);
        }

        await using (var connection = new NpgsqlConnection(disabledDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.customers;"));
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.wallets;"));
            Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.transactions;"));
        }

        await using var enabledDatabase = await TestDatabase.DisposableDatabase.CreateAsync("finviet_bootstrap_test");
        Skip.If(enabledDatabase is null, TestDatabase.DisposableDatabase.SkipReason);

        await using (var context = TestDatabase.CreateDbContext(enabledDatabase!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                enabledDatabase.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: true),
                new TestDatabase.TestHostEnvironment(Environments.Development),
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
        await using var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_bootstrap_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);

        async Task InitializeAsync()
        {
            await using var context = TestDatabase.CreateDbContext(database!.ConnectionString);
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                BuildConfiguration(seedDemoData: false, adminPassword: "IntegrationOnly!2026"),
                new TestDatabase.TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        await Task.WhenAll(InitializeAsync(), InitializeAsync());

        await using var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(3, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.schema_versions;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.admins;"));
        Assert.Equal(18, await ScalarAsync<long>(connection, "SELECT count(*) FROM public.categories;"));
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
}
