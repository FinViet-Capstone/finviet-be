using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
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

    public PgVectorRagRetriever(FinVietDbContext db, IEmbeddingService embeddings)
    {
        _db = db;
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<RagHit>> RetrieveAsync(
        Guid customerId, string query, int k = 5, CancellationToken cancellationToken = default)
    {
        var values = await _embeddings.EmbedAsync(query, cancellationToken);
        var queryVector = new Vector(values);

        var rows = await _db.RagChunks
            .Where(c => c.CustomerId == customerId || c.CustomerId == null)
            .OrderBy(c => c.Embedding.CosineDistance(queryVector))
            .Take(k)
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
            .ToList();
    }
}
