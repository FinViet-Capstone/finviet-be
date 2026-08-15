using System.Text;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pgvector;
using UglyToad.PdfPig;

namespace FinViet.Infrastructure.ExternalServices.Documents;

/// <summary>
/// Ingests sources into the RAG corpus. PDFs become GLOBAL knowledge documents
/// (customer_id NULL); weekly-report narratives become per-customer documents. Each source is
/// split into overlapping character windows, embedded per chunk, and persisted as
/// rag_document + rag_chunk rows.
/// </summary>
public class PdfDocumentIngestionService : IDocumentIngestionService
{
    private const int ChunkSize = 800;
    private const int ChunkOverlap = 150;
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    private readonly FinVietDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly string _documentsDirectory;
    private readonly string _baseUrlPath;

    public PdfDocumentIngestionService(FinVietDbContext db, IEmbeddingService embeddings, IConfiguration config)
    {
        _db = db;
        _embeddings = embeddings;

        // AppSettings:WebRootPath is configured as "" (not absent), so a plain `??` fallback
        // never triggers — an empty string is a valid non-null value.
        var configuredWebRoot = config["AppSettings:WebRootPath"];
        var webRoot = string.IsNullOrWhiteSpace(configuredWebRoot)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : configuredWebRoot;
        _documentsDirectory = Path.Combine(webRoot, "documents");
        _baseUrlPath = "/documents";

        Directory.CreateDirectory(_documentsDirectory);
    }

    public async Task<Guid> IngestPdfAsync(
        Stream pdfStream, string title, CancellationToken cancellationToken = default)
    {
        if (pdfStream is null)
            throw new BadRequestException("Tệp PDF không hợp lệ.");

        using var buffer = new MemoryStream();
        await pdfStream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (bytes.Length < PdfMagicBytes.Length || !bytes.AsSpan(0, PdfMagicBytes.Length).SequenceEqual(PdfMagicBytes))
            throw new BadRequestException("Tệp không phải PDF hợp lệ.");

        var text = ExtractPdfText(new MemoryStream(bytes));
        if (string.IsNullOrWhiteSpace(text))
            throw new BadRequestException("Không trích xuất được nội dung từ PDF.");

        var documentId = Guid.NewGuid();
        var fileName = $"{documentId:N}.pdf";
        await File.WriteAllBytesAsync(Path.Combine(_documentsDirectory, fileName), bytes, cancellationToken);

        var document = new RagDocument
        {
            Id = documentId,
            CustomerId = null,
            SourceType = "pdf",
            Title = string.IsNullOrWhiteSpace(title) ? "Tài liệu" : title.Trim(),
            Uri = $"{_baseUrlPath}/{fileName}",
            CreatedAt = DateTime.UtcNow
        };
        _db.RagDocuments.Add(document);

        await EmbedAndAddChunksAsync(document, text, customerId: null, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return document.Id;
    }

    public async Task IndexWeeklyReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.AiWeeklyReports
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);
        if (report is null || string.IsNullOrWhiteSpace(report.Narrative))
            return;

        // Idempotency: drop any prior document indexed for this report (uri = report:{id}).
        var marker = $"report:{reportId}";
        var existing = await _db.RagDocuments
            .Where(d => d.Uri == marker)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
            _db.RagDocuments.RemoveRange(existing);

        var document = new RagDocument
        {
            Id = Guid.NewGuid(),
            CustomerId = report.CustomerId,
            SourceType = "weekly_report",
            Title = $"Báo cáo tuần {report.WeekStart:dd/MM} - {report.WeekStart.AddDays(6):dd/MM/yyyy}",
            Uri = marker,
            CreatedAt = DateTime.UtcNow
        };
        _db.RagDocuments.Add(document);

        await EmbedAndAddChunksAsync(document, report.Narrative, report.CustomerId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EmbedAndAddChunksAsync(
        RagDocument document, string text, Guid? customerId, CancellationToken ct)
    {
        foreach (var chunk in Chunk(text))
        {
            var values = await _embeddings.EmbedAsync(
                chunk,
                ct,
                new AiRequestContext("rag_document_embedding", customerId));
            _db.RagChunks.Add(new RagChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                CustomerId = customerId,
                Content = chunk,
                Embedding = new Vector(values),
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static string ExtractPdfText(Stream pdfStream)
    {
        using var pdf = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    /// <summary>Split text into overlapping character windows on whitespace boundaries.</summary>
    private static IEnumerable<string> Chunk(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            yield break;

        var start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(ChunkSize, normalized.Length - start);
            var end = start + length;

            // Prefer breaking at the last space within the window to avoid splitting words.
            if (end < normalized.Length)
            {
                var lastSpace = normalized.LastIndexOf(' ', end - 1, length);
                if (lastSpace > start)
                    end = lastSpace;
            }

            var slice = normalized[start..end].Trim();
            if (slice.Length > 0)
                yield return slice;

            if (end >= normalized.Length)
                yield break;

            start = Math.Max(end - ChunkOverlap, start + 1);
        }
    }
}
