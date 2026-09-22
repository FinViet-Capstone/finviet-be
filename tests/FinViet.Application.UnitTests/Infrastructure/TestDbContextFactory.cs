using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinViet.Application.UnitTests.Infrastructure;

internal static class TestDbContextFactory
{
    internal static FinVietDbContext Create()
    {
        var options = new DbContextOptionsBuilder<FinVietDbContext>()
            .UseInMemoryDatabase($"finviet-unit-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new InMemoryFinVietDbContext(options);
    }

    private sealed class InMemoryFinVietDbContext : FinVietDbContext
    {
        internal InMemoryFinVietDbContext(DbContextOptions<FinVietDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<RagChunk>().Ignore(x => x.Embedding);
        }
    }
}
