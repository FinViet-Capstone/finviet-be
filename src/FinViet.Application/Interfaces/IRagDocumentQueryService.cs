using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IRagDocumentQueryService
{
    /// <summary>Lists GLOBAL knowledge documents (uploaded PDFs) newest first, with chunk counts.
    /// Per-customer narratives (weekly reports) are private corpus entries, never listed here.</summary>
    Task<IReadOnlyList<RagDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the original uploaded file of a global knowledge document. Throws
    /// <see cref="Common.Exceptions.NotFoundException"/> when the document doesn't exist, isn't
    /// global, or predates file storage.</summary>
    Task<RagDocumentFileResponse> GetDocumentFileAsync(Guid documentId, CancellationToken cancellationToken = default);
}
