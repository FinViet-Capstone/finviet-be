namespace FinViet.Application.Interfaces;

/// <summary>Turns text into an embedding vector via the embedding model (Gemini
/// text-embedding-004). Throws <see cref="Exceptions.GeminiUnavailableException"/> on
/// transport/parse failure so callers can fall back gracefully.</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
