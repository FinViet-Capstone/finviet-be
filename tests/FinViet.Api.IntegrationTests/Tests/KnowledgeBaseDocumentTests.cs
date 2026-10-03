using System.Net.Http.Headers;
using System.Text;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

/// <summary>Admin knowledge base: uploaded PDFs are retrievable from the database (not the
/// ephemeral container disk), and only global PDFs are listed.</summary>
public class KnowledgeBaseDocumentTests : ApiTestBase
{
    public KnowledgeBaseDocumentTests(ApiTestFixture fx) : base(fx) { }

    // TC-KB-01 — unauthenticated file download is rejected
    [SkippableFact]
    public async Task NoToken_GetDocumentFile_Returns401()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Get, $"/api/ai/documents/{Guid.NewGuid()}/file");
        Assert.Equal(401, r.Code);
    }

    // TC-KB-02 — customers cannot download knowledge base originals
    [SkippableFact]
    public async Task Customer_GetDocumentFile_Returns403()
    {
        RequireServer();
        var r = await CustGet($"/api/ai/documents/{Guid.NewGuid()}/file");
        Assert.Equal(403, r.Code);
    }

    // TC-KB-03 — unknown document id is a 404, not a 500
    [SkippableFact]
    public async Task Admin_GetDocumentFile_UnknownId_Returns404()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet($"/api/ai/documents/{Guid.NewGuid()}/file");
        Assert.Equal(404, r.Code);
    }

    // TC-KB-04 — an uploaded PDF is listed with hasFile=true and downloads byte-for-byte
    [SkippableFact]
    public async Task Admin_UploadedPdf_IsListedAndDownloadsIdentically()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var pdf = MinimalPdf("FinViet knowledge base integration test document");

        var upload = await Fx.Client.UploadFileAsync(
            "/api/ai/documents", "file", "kb-integration-test.pdf", pdf, "application/pdf", Admin);
        Skip.If(upload.Code == 502, $"Embedding provider unavailable on this server: {upload.Message}");
        Assert.Equal(200, upload.Code);
        var documentId = ApiTestFixture.Data(upload)!.GetValue<string>();

        var list = await AdminGet("/api/ai/documents");
        Assert.Equal(200, list.Code);
        var listed = ApiTestFixture.Data(list)!.AsArray()
            .Single(d => d!["id"]!.GetValue<string>() == documentId)!;
        Assert.True(listed["hasFile"]!.GetValue<bool>());
        Assert.Equal($"/api/ai/documents/{documentId}/file", listed["uri"]!.GetValue<string>());
        Assert.All(ApiTestFixture.Data(list)!.AsArray(), d => Assert.Equal("pdf", d!["sourceType"]!.GetValue<string>()));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/documents/{documentId}/file");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Admin);
        using var response = await Fx.Client.SendAsync(request);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>A single-page PDF with one line of Helvetica text and a correct xref table.</summary>
    private static byte[] MinimalPdf(string text)
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
