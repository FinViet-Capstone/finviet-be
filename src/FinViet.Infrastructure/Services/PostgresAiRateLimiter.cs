using System.Data;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinViet.Infrastructure.Services;

/// <summary>Atomic fixed-window limiter persisted in PostgreSQL and shared by all API instances.</summary>
public sealed class PostgresAiRateLimiter : IAiRateLimiter
{
    private readonly FinVietDbContext _db;
    private readonly AiLimitsOptions _limits;

    public PostgresAiRateLimiter(
        FinVietDbContext db,
        IOptions<AiLimitsOptions> limits)
    {
        _db = db;
        _limits = limits.Value;
    }

    public async Task<bool> TryAcquireAsync(
        Guid customerId,
        string feature,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feature))
            throw new ArgumentException("AI feature is required.", nameof(feature));

        var now = DateTime.UtcNow;
        var minuteStart = new DateTime(
            now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var minuteAllowed = await IncrementWindowAsync(
            customerId,
            feature,
            "minute",
            minuteStart,
            _limits.PerUserPerMinute,
            cancellationToken);
        if (!minuteAllowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var dayAllowed = await IncrementWindowAsync(
            customerId,
            feature,
            "day",
            dayStart,
            _limits.PerUserPerDay,
            cancellationToken);
        if (!dayAllowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> IncrementWindowAsync(
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

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(sql, connection)
        {
            Transaction = (NpgsqlTransaction)_db.Database.CurrentTransaction!.GetDbTransaction()
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
