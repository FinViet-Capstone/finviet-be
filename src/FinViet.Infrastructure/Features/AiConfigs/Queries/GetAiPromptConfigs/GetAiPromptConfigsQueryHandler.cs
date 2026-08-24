using FinViet.Application.Common;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigs;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.AiConfigs.Queries.GetAiPromptConfigs;

public class GetAiPromptConfigsQueryHandler
    : IRequestHandler<GetAiPromptConfigsQuery, IReadOnlyList<AiPromptConfigDto>>
{
    private readonly FinVietDbContext _db;

    public GetAiPromptConfigsQueryHandler(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<AiPromptConfigDto>> Handle(
        GetAiPromptConfigsQuery request,
        CancellationToken cancellationToken)
    {
        var configs = await _db.AiPromptConfigs
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var adminIds = configs
            .Where(c => c.UpdatedBy.HasValue)
            .Select(c => c.UpdatedBy!.Value)
            .Distinct()
            .ToList();
        var usernames = await _db.Admins
            .AsNoTracking()
            .Where(a => adminIds.Contains(a.AdminId))
            .ToDictionaryAsync(a => a.AdminId, a => a.Username, cancellationToken);

        var displayOrder = AiPromptFeatures.All
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index);

        return configs
            .OrderBy(c => displayOrder.TryGetValue(c.FeatureKey, out var index) ? index : int.MaxValue)
            .Select(c => new AiPromptConfigDto(
                c.FeatureKey,
                c.DisplayName,
                c.PersonaInstruction,
                c.Temperature,
                c.MaxOutputTokens,
                c.UpdatedBy,
                c.UpdatedBy.HasValue && usernames.TryGetValue(c.UpdatedBy.Value, out var username)
                    ? username
                    : null,
                c.UpdatedAt))
            .ToList();
    }
}
