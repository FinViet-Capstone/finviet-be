using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigHistory;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.AiConfigs.Queries.GetAiPromptConfigHistory;

public class GetAiPromptConfigHistoryQueryHandler
    : IRequestHandler<GetAiPromptConfigHistoryQuery, IReadOnlyList<AiPromptConfigHistoryDto>>
{
    // Newest-first timeline for the admin screen; 50 snapshots is far more than a prompt-tuning
    // history realistically accumulates, without an unbounded query.
    private const int MaxEntries = 50;

    private readonly FinVietDbContext _db;

    public GetAiPromptConfigHistoryQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<AiPromptConfigHistoryDto>> Handle(
        GetAiPromptConfigHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var exists = await _db.AiPromptConfigs
            .AsNoTracking()
            .AnyAsync(c => c.FeatureKey == request.FeatureKey, cancellationToken);
        if (!exists)
            throw new NotFoundException("AiPromptConfig", request.FeatureKey);

        var entries = await _db.AiPromptConfigHistories
            .AsNoTracking()
            .Where(h => h.FeatureKey == request.FeatureKey)
            .OrderByDescending(h => h.ChangedAt)
            .Take(MaxEntries)
            .ToListAsync(cancellationToken);

        var adminIds = entries
            .Where(h => h.ChangedBy.HasValue)
            .Select(h => h.ChangedBy!.Value)
            .Distinct()
            .ToList();
        var usernames = await _db.Admins
            .AsNoTracking()
            .Where(a => adminIds.Contains(a.AdminId))
            .ToDictionaryAsync(a => a.AdminId, a => a.Username, cancellationToken);

        return entries
            .Select(h => new AiPromptConfigHistoryDto(
                h.Id,
                h.FeatureKey,
                h.PersonaInstruction,
                h.Temperature,
                h.MaxOutputTokens,
                h.ChangedBy,
                h.ChangedBy.HasValue && usernames.TryGetValue(h.ChangedBy.Value, out var username)
                    ? username
                    : null,
                h.ChangedAt))
            .ToList();
    }
}
