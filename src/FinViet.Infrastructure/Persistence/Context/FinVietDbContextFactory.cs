using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Context;

internal sealed class FinVietDbContextFactory : IDbContextFactory<FinVietDbContext>
{
    private readonly NpgsqlDataSource _dataSource;

    public FinVietDbContextFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public FinVietDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinVietDbContext>()
            .UseNpgsql(_dataSource, o => o.UseVector())
            .Options;
        return new FinVietDbContext(options);
    }
}
