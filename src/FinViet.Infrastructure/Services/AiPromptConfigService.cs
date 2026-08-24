using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// Reads admin-tunable prompt settings from ai_prompt_configs with a short in-memory cache so
/// every generation call doesn't add a database round trip. The update command handler calls
/// Invalidate, and the TTL bounds staleness if an update happens on another instance.
/// </summary>
public sealed class AiPromptConfigService : IAiPromptConfigProvider
{
    private const string CacheKeyPrefix = "ai_prompt_config:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<FinVietDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public AiPromptConfigService(IDbContextFactory<FinVietDbContext> contextFactory, IMemoryCache cache)
    {
        _contextFactory = contextFactory;
        _cache = cache;
    }

    public async Task<AiPromptRuntimeConfig> GetAsync(
        string featureKey,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKeyPrefix + featureKey, out AiPromptRuntimeConfig? cached)
            && cached is not null)
        {
            return cached;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.AiPromptConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.FeatureKey == featureKey, cancellationToken);

        var config = row is null
            ? AiPromptDefaults.For(featureKey)
            : new AiPromptRuntimeConfig(row.PersonaInstruction, (double)row.Temperature, row.MaxOutputTokens);

        _cache.Set(CacheKeyPrefix + featureKey, config, CacheTtl);
        return config;
    }

    public void Invalidate(string featureKey) => _cache.Remove(CacheKeyPrefix + featureKey);
}
