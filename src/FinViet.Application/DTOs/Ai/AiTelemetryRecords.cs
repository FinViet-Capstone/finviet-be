namespace FinViet.Application.DTOs.Ai;

public sealed record AiRequestContext(
    string Feature,
    Guid? CustomerId = null,
    Guid? SessionId = null);

public sealed record AiUsageRecord(
    string Feature,
    string Provider,
    string Outcome,
    Guid? CustomerId = null,
    Guid? SessionId = null,
    string? Model = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? TotalTokens = null,
    int? LatencyMs = null,
    string? ProviderRequestId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AiAuditRecord(
    string EventType,
    string ActorType,
    Guid? CustomerId = null,
    Guid? SessionId = null,
    Guid? CorrelationId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);
