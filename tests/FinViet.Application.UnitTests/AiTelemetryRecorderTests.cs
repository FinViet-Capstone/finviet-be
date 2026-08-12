using FinViet.Application.DTOs.Ai;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Application.UnitTests;

public class AiTelemetryRecorderTests
{
    [Fact]
    public async Task RecordUsageAsync_PersistsOnlySuppliedOperationalMetadata()
    {
        var databaseName = $"finviet-telemetry-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<FinVietDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var factory = new InMemoryContextFactory(options);
        await using (var setup = factory.CreateDbContext())
        {
            var customer = Customer();
            setup.Customers.Add(customer);
            await setup.SaveChangesAsync();
        }
        var customerId = (await ReadCustomerAsync(factory)).CustomerId;
        var recorder = new AiTelemetryRecorder(
            factory,
            NullLogger<AiTelemetryRecorder>.Instance);

        await recorder.RecordUsageAsync(new AiUsageRecord(
            "chat",
            "gemini",
            "success",
            customerId,
            Model: "gemini-3.6-flash",
            InputTokens: 10,
            OutputTokens: 5,
            TotalTokens: 15,
            LatencyMs: 42,
            ProviderRequestId: "request-1",
            Metadata: new Dictionary<string, object?> { ["historyEnabled"] = false }));

        await using var verification = factory.CreateDbContext();
        var row = await verification.AiUsageEvents.SingleAsync();
        Assert.Equal("chat", row.Feature);
        Assert.Equal("gemini", row.Provider);
        Assert.Equal("success", row.Outcome);
        Assert.Contains("historyEnabled", row.Metadata);
        Assert.DoesNotContain("prompt", row.Metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", row.Metadata, StringComparison.OrdinalIgnoreCase);
    }

    private static Customer Customer()
    {
        var customerId = Guid.NewGuid();
        return new Customer
        {
            CustomerId = customerId,
            Email = $"{customerId:N}@finviet.local",
            FullName = "Telemetry Customer",
            IsActive = true
        };
    }

    private static async Task<Customer> ReadCustomerAsync(InMemoryContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.Customers.SingleAsync();
    }

    private sealed class InMemoryContextFactory : IDbContextFactory<FinVietDbContext>
    {
        private readonly DbContextOptions<FinVietDbContext> _options;

        public InMemoryContextFactory(DbContextOptions<FinVietDbContext> options) => _options = options;

        public FinVietDbContext CreateDbContext() => new TelemetryDbContext(_options);
    }

    private sealed class TelemetryDbContext : FinVietDbContext
    {
        public TelemetryDbContext(DbContextOptions<FinVietDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RagChunk>().Ignore(x => x.Embedding);
        }
    }
}
