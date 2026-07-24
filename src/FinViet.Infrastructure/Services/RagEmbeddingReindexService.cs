using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace FinViet.Infrastructure.Services;

/// <summary>Re-embeds existing chunks in place. Retrieval must remain disabled until this completes.</summary>
public class RagEmbeddingReindexService : IRagEmbeddingReindexService
{
    private const int BatchSize = 25;

    private readonly FinVietDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<RagEmbeddingReindexService> _logger;

    public RagEmbeddingReindexService(
        FinVietDbContext db,
        IEmbeddingService embeddings,
        ILogger<RagEmbeddingReindexService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<int> ReindexAsync(CancellationToken cancellationToken = default)
    {
        var chunkIds = await _db.RagChunks
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        var total = chunkIds.Count;
        _logger.LogInformation("Starting RAG embedding re-index for {Total} chunks.", total);

        var processed = 0;
        foreach (var idBatch in chunkIds.Chunk(BatchSize))
        {
            var batch = await _db.RagChunks
                .AsNoTracking()
                .Where(c => idBatch.Contains(c.Id))
                .Select(c => new { c.Id, c.Content })
                .ToListAsync(cancellationToken);

            if (batch.Count != idBatch.Length)
            {
                throw new InvalidOperationException(
                    "The RAG corpus changed during re-index. Keep RAG disabled and retry.");
            }

            foreach (var row in batch)
            {
                var values = await _embeddings.EmbedAsync(row.Content, cancellationToken);
                var chunk = new RagChunk
                {
                    Id = row.Id,
                    Embedding = new Vector(values)
                };
                _db.Attach(chunk);
                _db.Entry(chunk).Property(c => c.Embedding).IsModified = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
            processed += batch.Count;
            _db.ChangeTracker.Clear();
            _logger.LogInformation("Re-indexed {Processed}/{Total} RAG chunks.", processed, total);
        }

        var finalIds = await _db.RagChunks
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (!chunkIds.SequenceEqual(finalIds))
        {
            throw new InvalidOperationException(
                "The RAG corpus changed during re-index. Keep RAG disabled and retry.");
        }

        await _db.Database.ExecuteSqlRawAsync(
            "REINDEX INDEX ix_rag_chunk_embedding;",
            cancellationToken);

        _logger.LogInformation("RAG embedding re-index completed for {Processed} chunks.", processed);
        return processed;
    }
}
