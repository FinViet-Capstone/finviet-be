using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class RagDocumentQueryService : IRagDocumentQueryService
{
    private const string PdfSourceType = "pdf";

    private readonly FinVietDbContext _db;
    public RagDocumentQueryService(FinVietDbContext db) => _db = db;

    public async Task<IReadOnlyList<RagDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.RagDocuments
            .AsNoTracking()
            .Where(d => d.CustomerId == null && d.SourceType == PdfSourceType)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new RagDocumentResponse
            {
                Id = d.Id,
                Title = d.Title,
                SourceType = d.SourceType,
                Uri = d.Uri,
                CreatedAt = d.CreatedAt,
                ChunkCount = d.Chunks.Count,
                HasFile = d.File != null
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RagDocumentFileResponse> GetDocumentFileAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        var file = await _db.RagDocumentFiles
            .AsNoTracking()
            .Where(f => f.DocumentId == documentId
                && f.Document.CustomerId == null
                && f.Document.SourceType == PdfSourceType)
            .Select(f => new RagDocumentFileResponse { Content = f.Content, ContentType = f.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        return file ?? throw new NotFoundException(
            "Không tìm thấy file gốc của tài liệu này. Vui lòng tải lên lại tài liệu.");
    }
}
