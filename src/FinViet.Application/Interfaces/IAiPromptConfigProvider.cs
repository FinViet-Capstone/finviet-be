using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>
/// Serves the admin-tunable prompt settings (persona, temperature, output-token cap) for one AI
/// feature, falling back to built-in defaults when no row exists. Implementations cache reads;
/// Invalidate is called after an admin update so the next generation picks up the new values.
/// </summary>
public interface IAiPromptConfigProvider
{
    Task<AiPromptRuntimeConfig> GetAsync(string featureKey, CancellationToken cancellationToken = default);

    void Invalidate(string featureKey);
}
