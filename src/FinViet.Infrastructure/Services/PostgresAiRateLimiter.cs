using System.Data;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinViet.Infrastructure.Services;

/// <summary>Atomic fixed-window limiter persisted in PostgreSQL and shared by all API instances.
/// Uses <see cref="IDbContextFactory{TContext}"/> rather than an injected scoped context so it's
/// safe to call concurrently — e.g. from bulk-import's parallel per-row classification, where many
/// TryAcquireAsync calls can be in flight at once within the same request.</summary>
public sealed class PostgresAiRateLimiter : IAiRateLimiter
{
    private const string BulkImportFeature = "classification_batch";

    private readonly IDbContextFactory<FinVietDbContext> _dbFactory;
    private readonly AiLimitsOptions _limits;

    public PostgresAiRateLimiter(
        IDbContextFactory<FinVietDbContext> dbFactory,
        IOptions<AiLimitsOptions> limits)
    {
        _dbFactory = dbFactory;
        _limits = limits.Value;
    }

    public async Task<bool> TryAcquireAsync(
        Guid customerId,
        string feature,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feature))
            throw new ArgumentException("AI feature is required.", nameof(feature));

        var (perMinute, perDay) = string.Equals(feature, BulkImportFeature, StringComparison.OrdinalIgnoreCase)
            ? (_limits.BulkImportPerMinute, _limits.BulkImportPerDay)
            : (_limits.PerUserPerMinute, _limits.PerUserPerDay);

        var now = DateTime.UtcNow;
        var minuteStart = new DateTime(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var minuteAllowed = await IncrementWindowAsync(
            db,
            customerId,
            feature,
            "minute",
            minuteStart,
            perMinute,
            cancellationToken);
        if (!minuteAllowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var dayAllowed = await IncrementWindowAsync(
            db,
            customerId,
            feature,
            "day",
            dayStart,
            perDay,
            cancellationToken);
        if (!dayAllowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> IncrementWindowAsync(
        FinVietDbContext db,
        Guid customerId,
        string feature,
        string windowType,
        DateTime windowStart,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ai_rate_limit_windows
                (customer_id, feature, window_type, window_start, request_count, updated_at)
            VALUES
                (@customerId, @feature, @windowType, @windowStart, 1, now())
            ON CONFLICT (customer_id, feature, window_type, window_start)
            DO UPDATE SET
                request_count = ai_rate_limit_windows.request_count + 1,
                updated_at = now()
            WHERE ai_rate_limit_windows.request_count < @limit
            RETURNING request_count;
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            Transaction = (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction()
        };
        command.Parameters.AddWithValue("customerId", customerId);
        command.Parameters.AddWithValue("feature", feature.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("windowType", windowType);
        command.Parameters.AddWithValue("windowStart", windowStart);
        command.Parameters.AddWithValue("limit", Math.Max(limit, 1));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }
}
