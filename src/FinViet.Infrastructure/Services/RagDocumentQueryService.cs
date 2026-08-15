using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class RagDocumentQueryService : IRagDocumentQueryService
{
    private readonly FinVietDbContext _db;
    public RagDocumentQueryService(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<RagDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.RagDocuments
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new RagDocumentResponse
            {
                Id = d.Id,
                Title = d.Title,
                SourceType = d.SourceType,
                Uri = d.Uri,
                CreatedAt = d.CreatedAt,
                ChunkCount = d.Chunks.Count
            })
            .ToListAsync(cancellationToken);
    }
}
