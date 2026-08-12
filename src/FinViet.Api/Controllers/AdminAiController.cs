using FinViet.Application.Common;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/ai")]
public class AdminAiController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;

    public AdminAiController(IDocumentIngestionService ingestion)
    {
        _ingestion = ingestion;
    }

    /// <summary>Ingest a finance PDF into the global knowledge corpus available to customer chats.</summary>
    [HttpPost("documents")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<Guid>>> IngestDocument(
        IFormFile file,
        [FromForm] string? title,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<Guid>.Fail("Vui lòng chọn tệp PDF."));

        await using var stream = file.OpenReadStream();
        var documentId = await _ingestion.IngestPdfAsync(
            stream,
            string.IsNullOrWhiteSpace(title) ? file.FileName : title,
            cancellationToken);

        return Ok(ApiResponse<Guid>.Ok(documentId, "Đã nạp tài liệu vào kho tri thức."));
    }
}
