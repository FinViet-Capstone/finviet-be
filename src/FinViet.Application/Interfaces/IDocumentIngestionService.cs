namespace FinViet.Application.Interfaces;

/// <summary>Ingests sources into the RAG corpus: chunk → embed → persist as
/// rag_document + rag_chunk rows. Global knowledge docs (PDFs) are visible to all
/// customers; weekly-report/monthly-summary narratives are per-customer.</summary>
public interface IDocumentIngestionService
{
    /// <summary>Ingest a PDF as a GLOBAL knowledge document. Returns the new document id.</summary>
    Task<Guid> IngestPdfAsync(
        Stream pdfStream, string title, CancellationToken cancellationToken = default);

    /// <summary>Index a saved weekly report's narrative into the customer's personal corpus.
    /// Best-effort: callers should not fail the surrounding operation if this throws.</summary>
    Task IndexWeeklyReportAsync(Guid reportId, CancellationToken cancellationToken = default);
}
