using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.AiConfigs.Commands.UpdateAiPromptConfig;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.AiConfigs.Commands.UpdateAiPromptConfig;

public class UpdateAiPromptConfigCommandHandler
    : IRequestHandler<UpdateAiPromptConfigCommand, AiPromptConfigDto>
{
    private readonly FinVietDbContext _db;
    private readonly IAiPromptConfigProvider _promptConfigs;

    public UpdateAiPromptConfigCommandHandler(FinVietDbContext db, IAiPromptConfigProvider promptConfigs)
    {
        _db = db;
        _promptConfigs = promptConfigs;
    }

    public async Task<AiPromptConfigDto> Handle(
        UpdateAiPromptConfigCommand request,
        CancellationToken cancellationToken)
    {
        var config = await _db.AiPromptConfigs
            .FirstOrDefaultAsync(c => c.FeatureKey == request.FeatureKey, cancellationToken)
            ?? throw new NotFoundException("AiPromptConfig", request.FeatureKey);

        config.PersonaInstruction = request.Request.PersonaInstruction.Trim();
        config.Temperature = request.Request.Temperature;
        config.MaxOutputTokens = request.Request.MaxOutputTokens;
        config.UpdatedBy = request.AdminId;
        config.UpdatedAt = DateTime.UtcNow;

        // One snapshot per state, written in the same SaveChanges as the update itself.
        _db.AiPromptConfigHistories.Add(new AiPromptConfigHistory
        {
            Id = Guid.NewGuid(),
            FeatureKey = config.FeatureKey,
            PersonaInstruction = config.PersonaInstruction,
            Temperature = config.Temperature,
            MaxOutputTokens = config.MaxOutputTokens,
            ChangedBy = request.AdminId,
            ChangedAt = config.UpdatedAt
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Next generation call re-reads the fresh row instead of the cached values.
        _promptConfigs.Invalidate(config.FeatureKey);

        var username = await _db.Admins
            .AsNoTracking()
            .Where(a => a.AdminId == request.AdminId)
            .Select(a => a.Username)
            .FirstOrDefaultAsync(cancellationToken);

        return new AiPromptConfigDto(
            config.FeatureKey,
            config.DisplayName,
            config.PersonaInstruction,
            config.Temperature,
            config.MaxOutputTokens,
            config.UpdatedBy,
            username,
            config.UpdatedAt);
    }
}
