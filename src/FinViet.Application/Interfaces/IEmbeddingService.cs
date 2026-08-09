namespace FinViet.Application.Interfaces;

/// <summary>Turns text into a fixed-size embedding vector through the configured provider.
/// Throws <see cref="Exceptions.AiProviderUnavailableException"/> on transport/parse failure so
/// callers can fall back gracefully.</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
