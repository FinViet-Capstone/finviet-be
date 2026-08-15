using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IRagDocumentQueryService
{
    /// <summary>Lists RAG documents (global + per-customer) newest first, with chunk counts.</summary>
    Task<IReadOnlyList<RagDocumentResponse>> GetDocumentsAsync(CancellationToken cancellationToken = default);
}
