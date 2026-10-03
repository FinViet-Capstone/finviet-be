using FinViet.Application.Common.Exceptions;
using FinViet.Infrastructure.IntegrationTests.Support;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Infrastructure.IntegrationTests;

/// <summary>Exercises V0014's rag_document_file table through the real EF mapping: bytea round
/// trip, the hasFile projection, and cascade delete — none of which the InMemory provider checks.</summary>
public sealed class RagDocumentFileStorageTests
{
    [SkippableFact]
    public async Task StoredFile_RoundTripsThroughQueryService_AndCascadesWithItsDocument()
    {
        await using var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_rag_file_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);

        await using (var context = TestDatabase.CreateDbContext(database!.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                context,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:SeedDemoData"] = "false",
                        ["Admin:DefaultPassword"] = "IntegrationOnly!2026"
                    })
                    .Build(),
                new TestDatabase.TestHostEnvironment(Environments.Production),
                NullLogger.Instance);
        }

        var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0xFF };
        var withFile = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        await using (var context = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            context.RagDocuments.Add(new RagDocument
            {
                Id = withFile,
                SourceType = "pdf",
                Title = "stored",
                CreatedAt = DateTime.UtcNow,
                File = new RagDocumentFile
                {
                    DocumentId = withFile,
                    Content = content,
                    ContentType = "application/pdf",
                    SizeBytes = content.Length,
                    CreatedAt = DateTime.UtcNow
                }
            });
            context.RagDocuments.Add(new RagDocument
            {
                Id = legacy,
                SourceType = "pdf",
                Title = "legacy",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();
        }

        await using (var context = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            var service = new RagDocumentQueryService(context);

            var documents = await service.GetDocumentsAsync();
            Assert.Equal([(withFile, true), (legacy, false)], documents.Select(d => (d.Id, d.HasFile)));

            var file = await service.GetDocumentFileAsync(withFile);
            Assert.Equal(content, file.Content);
            Assert.Equal("application/pdf", file.ContentType);

            await Assert.ThrowsAsync<NotFoundException>(() => service.GetDocumentFileAsync(legacy));

            await context.RagDocuments.Where(d => d.Id == withFile).ExecuteDeleteAsync();
            Assert.False(await context.RagDocumentFiles.AnyAsync(f => f.DocumentId == withFile));
        }
    }
}
