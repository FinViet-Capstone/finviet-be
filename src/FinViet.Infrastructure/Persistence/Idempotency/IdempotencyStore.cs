using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinViet.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Persists idempotency results in the same database transaction as the money movement.
/// A retry therefore receives the original response and never performs the debit twice.
/// </summary>
internal static class IdempotencyStore
{
    private const int MaximumKeyLength = 200;

    internal sealed record Claim(bool IsReplay, string? ResponsePayload);

    public static string ComputeRequestHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static async Task<Claim> ClaimAsync(
        FinVietDbContext db,
        Guid customerId,
        string operation,
        string? idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new BadRequestException("Idempotency-Key header is required for money-changing requests.");

        var key = idempotencyKey.Trim();
        if (key.Length > MaximumKeyLength)
            throw new BadRequestException($"Idempotency-Key must not exceed {MaximumKeyLength} characters.");

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                insert.CommandText = """
                    INSERT INTO idempotency_keys (customer_id, operation, idempotency_key, request_hash)
                    VALUES (@customerId, @operation, @idempotencyKey, @requestHash)
                    ON CONFLICT (customer_id, operation, idempotency_key) DO NOTHING
                    RETURNING response_payload;
                    """;
                AddParameter(insert, "@customerId", customerId);
                AddParameter(insert, "@operation", operation);
                AddParameter(insert, "@idempotencyKey", key);
                AddParameter(insert, "@requestHash", requestHash);

                await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    return new Claim(false, null);
            }

            // A conflicting INSERT waits for the first transaction to commit. Reading the
            // completed response here makes concurrent retries safe as well as sequential ones.
            await using var select = connection.CreateCommand();
            select.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            select.CommandText = """
                SELECT request_hash, response_payload
                FROM idempotency_keys
                WHERE customer_id = @customerId
                  AND operation = @operation
                  AND idempotency_key = @idempotencyKey
                FOR UPDATE;
                """;
            AddParameter(select, "@customerId", customerId);
            AddParameter(select, "@operation", operation);
            AddParameter(select, "@idempotencyKey", key);

            await using var existing = await select.ExecuteReaderAsync(cancellationToken);
            if (!await existing.ReadAsync(cancellationToken))
                throw new ConflictException("Unable to claim the idempotency key. Retry the request.");

            var existingHash = existing.GetString(0);
            if (!string.Equals(existingHash, requestHash, StringComparison.Ordinal))
                throw new ConflictException("Idempotency-Key was already used with a different request payload.");

            if (existing.IsDBNull(1))
                throw new ConflictException("A request with this Idempotency-Key is still being processed.");

            return new Claim(true, existing.GetString(1));
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public static async Task CompleteAsync<T>(
        FinVietDbContext db,
        Guid customerId,
        string operation,
        string idempotencyKey,
        T response,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                UPDATE idempotency_keys
                SET response_payload = CAST(@responsePayload AS jsonb),
                    completed_at = now()
                WHERE customer_id = @customerId
                  AND operation = @operation
                  AND idempotency_key = @idempotencyKey;
                """;
            AddParameter(command, "@responsePayload", JsonSerializer.Serialize(response));
            AddParameter(command, "@customerId", customerId);
            AddParameter(command, "@operation", operation);
            AddParameter(command, "@idempotencyKey", idempotencyKey.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public static T ReadReplay<T>(Claim claim)
    {
        if (!claim.IsReplay || string.IsNullOrWhiteSpace(claim.ResponsePayload))
            throw new InvalidOperationException("The idempotency claim does not contain a replay response.");

        return JsonSerializer.Deserialize<T>(claim.ResponsePayload)
               ?? throw new InvalidOperationException("The stored idempotency response is invalid.");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
