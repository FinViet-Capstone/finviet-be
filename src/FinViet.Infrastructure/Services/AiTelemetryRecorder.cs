using System.Text.Json;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Services;

public sealed class AiTelemetryRecorder : IAiTelemetryRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<FinVietDbContext> _dbFactory;
    private readonly ILogger<AiTelemetryRecorder> _logger;

    public AiTelemetryRecorder(
        IDbContextFactory<FinVietDbContext> dbFactory,
        ILogger<AiTelemetryRecorder> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordUsageAsync(
        AiUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            db.AiUsageEvents.Add(new AiUsageEvent
            {
                Id = Guid.NewGuid(),
                CustomerId = record.CustomerId,
                SessionId = record.SessionId,
                Feature = Limit(record.Feature, 40),
                Provider = Limit(record.Provider, 40),
                Model = LimitNullable(record.Model, 120),
                Outcome = Limit(record.Outcome, 30),
                InputTokens = NonNegative(record.InputTokens),
                OutputTokens = NonNegative(record.OutputTokens),
                TotalTokens = NonNegative(record.TotalTokens),
                LatencyMs = NonNegative(record.LatencyMs),
                ProviderRequestId = LimitNullable(record.ProviderRequestId, 255),
                Metadata = SerializeMetadata(record.Metadata),
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist AI usage metadata for {Feature}.", record.Feature);
        }
    }

    public async Task RecordAuditAsync(
        AiAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            db.AiAuditEvents.Add(new AiAuditEvent
            {
                Id = Guid.NewGuid(),
                CustomerId = record.CustomerId,
                SessionId = record.SessionId,
                ActorType = Limit(record.ActorType, 20),
                EventType = Limit(record.EventType, 80),
                CorrelationId = record.CorrelationId,
                Metadata = SerializeMetadata(record.Metadata),
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist AI audit metadata for {EventType}.", record.EventType);
        }
    }

    private static string SerializeMetadata(IReadOnlyDictionary<string, object?>? metadata)
        => metadata is null || metadata.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(metadata, JsonOptions);

    private static int? NonNegative(int? value) => value is >= 0 ? value : null;

    private static string Limit(string value, int length)
    {
        var normalized = value.Trim();
        return normalized.Length <= length ? normalized : normalized[..length];
    }

    private static string? LimitNullable(string? value, int length)
        => string.IsNullOrWhiteSpace(value) ? null : Limit(value, length);
}
