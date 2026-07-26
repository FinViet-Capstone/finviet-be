namespace FinViet.Application.Interfaces;

/// <summary>Replaces every stored RAG embedding in place using the currently configured model.</summary>
public interface IRagEmbeddingReindexService
{
    Task<int> ReindexAsync(CancellationToken cancellationToken = default);
}
