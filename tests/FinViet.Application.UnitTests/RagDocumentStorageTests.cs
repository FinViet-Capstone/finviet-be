using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.ExternalServices.Documents;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Moq;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FinViet.Application.UnitTests;

/// <summary>Knowledge-base PDFs must survive container restarts (Render's disk is ephemeral), so the
/// original bytes live in Postgres next to the document, and the admin list only shows global PDFs —
/// never a customer's private weekly-report narrative.</summary>
public class RagDocumentStorageTests
{
    [Fact]
    public async Task IngestPdfAsync_StoresOriginalBytesWithTheDocument()
    {
        await using var db = TestDbContextFactory.Create();
        var pdf = BuildPdf("Personal finance basics: the 50/30/20 budgeting rule.");
        var service = new PdfDocumentIngestionService(db, Embeddings());

        var documentId = await service.IngestPdfAsync(new MemoryStream(pdf), "financialmanagement");

        var document = Assert.Single(db.RagDocuments);
        Assert.Equal(documentId, document.Id);
        Assert.Equal($"/api/ai/documents/{documentId}/file", document.Uri);
        var file = Assert.Single(db.RagDocumentFiles);
        Assert.Equal(documentId, file.DocumentId);
        Assert.Equal(pdf, file.Content);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(pdf.Length, file.SizeBytes);
        Assert.NotEmpty(db.RagChunks);
    }

    [Fact]
    public async Task IngestPdfAsync_NonPdf_StoresNothing()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new PdfDocumentIngestionService(db, Embeddings());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.IngestPdfAsync(new MemoryStream("not a pdf"u8.ToArray()), "x"));

        Assert.Empty(db.RagDocuments);
        Assert.Empty(db.RagDocumentFiles);
    }

    [Fact]
    public async Task GetDocumentsAsync_ReturnsOnlyGlobalPdfs_WithFileAvailability()
    {
        await using var db = TestDbContextFactory.Create();
        var stored = Document(sourceType: "pdf", customerId: null, created: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        var legacy = Document(sourceType: "pdf", customerId: null, created: new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc));
        var weeklyReport = Document(sourceType: "weekly_report", customerId: Guid.NewGuid(), created: new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc));
        db.RagDocuments.AddRange(stored, legacy, weeklyReport);
        db.RagDocumentFiles.Add(File(stored.Id, [1, 2, 3]));
        await db.SaveChangesAsync();

        var documents = await new RagDocumentQueryService(db).GetDocumentsAsync();

        Assert.Collection(documents,
            d => { Assert.Equal(stored.Id, d.Id); Assert.True(d.HasFile); },
            d => { Assert.Equal(legacy.Id, d.Id); Assert.False(d.HasFile); });
    }

    [Fact]
    public async Task GetDocumentFileAsync_ReturnsStoredBytes()
    {
        await using var db = TestDbContextFactory.Create();
        var document = Document(sourceType: "pdf", customerId: null);
        db.RagDocuments.Add(document);
        db.RagDocumentFiles.Add(File(document.Id, [0x25, 0x50, 0x44, 0x46]));
        await db.SaveChangesAsync();

        var file = await new RagDocumentQueryService(db).GetDocumentFileAsync(document.Id);

        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, file.Content);
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Fact]
    public async Task GetDocumentFileAsync_LegacyDocumentWithoutStoredFile_ThrowsNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var legacy = Document(sourceType: "pdf", customerId: null);
        db.RagDocuments.Add(legacy);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RagDocumentQueryService(db).GetDocumentFileAsync(legacy.Id));
    }

    [Fact]
    public async Task GetDocumentFileAsync_PerCustomerDocument_ThrowsNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var weeklyReport = Document(sourceType: "weekly_report", customerId: Guid.NewGuid());
        db.RagDocuments.Add(weeklyReport);
        db.RagDocumentFiles.Add(File(weeklyReport.Id, [1]));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RagDocumentQueryService(db).GetDocumentFileAsync(weeklyReport.Id));
    }

    [Fact]
    public async Task GetDocumentFileAsync_UnknownId_ThrowsNotFound()
    {
        await using var db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RagDocumentQueryService(db).GetDocumentFileAsync(Guid.NewGuid()));
    }

    private static IEmbeddingService Embeddings()
    {
        var embeddings = new Mock<IEmbeddingService>();
        embeddings
            .Setup(x => x.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<AiRequestContext?>()))
            .ReturnsAsync(new float[768]);
        return embeddings.Object;
    }

    private static byte[] BuildPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4)
            .AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        return builder.Build();
    }

    private static RagDocument Document(string sourceType, Guid? customerId, DateTime? created = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        SourceType = sourceType,
        Title = sourceType,
        CreatedAt = created ?? DateTime.UtcNow
    };

    private static RagDocumentFile File(Guid documentId, byte[] content) => new()
    {
        DocumentId = documentId,
        Content = content,
        ContentType = "application/pdf",
        SizeBytes = content.Length
    };
}
