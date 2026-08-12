using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

/// <summary>
/// pgvector-backed retrieval. Embeds the query (via <see cref="IEmbeddingService"/>) and orders
/// rag_chunk rows by cosine distance, scoped to the customer's own chunks plus global knowledge
/// chunks (customer_id IS NULL). Returns the top-K with a similarity score (1 - distance).
/// </summary>
public class PgVectorRagRetriever : IRagRetriever
{
    private readonly FinVietDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly GeminiOptions _options;

    public PgVectorRagRetriever(
        FinVietDbContext db,
        IEmbeddingService embeddings,
        IOptions<GeminiOptions> options)
    {
        _db = db;
        _embeddings = embeddings;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<RagHit>> RetrieveAsync(
        Guid customerId, string query, int k = 5, CancellationToken cancellationToken = default)
    {
        if (!_options.RagEnabled)
            return Array.Empty<RagHit>();

        var values = await _embeddings.EmbedAsync(
            query,
            cancellationToken,
            new AiRequestContext("rag_retrieval_embedding", customerId));
        var queryVector = new Vector(values);

        var resultLimit = Math.Clamp(k, 1, 20);
        var candidateLimit = Math.Min(resultLimit * 4, 80);
        var rows = await _db.RagChunks
            .Where(c => c.CustomerId == customerId || c.CustomerId == null)
            .OrderBy(c => c.Embedding.CosineDistance(queryVector))
            .Take(candidateLimit)
            .Select(c => new
            {
                c.Content,
                c.Document!.SourceType,
                c.Document.Title,
                Distance = c.Embedding.CosineDistance(queryVector)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new RagHit
            {
                Content = r.Content,
                SourceType = r.SourceType,
                Title = r.Title,
                Score = 1.0 - r.Distance
            })
            .Where(hit => hit.Score >= _options.RagMinimumSimilarity)
            .Take(resultLimit)
            .ToList();
    }
}
