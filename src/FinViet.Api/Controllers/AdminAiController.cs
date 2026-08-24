using System.Security.Claims;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Features.AiConfigs.Commands.UpdateAiPromptConfig;
using FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigHistory;
using FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigs;
using FinViet.Application.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/ai")]
public class AdminAiController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestion;
    private readonly IRagDocumentQueryService _documents;
    private readonly IMediator _mediator;

    public AdminAiController(
        IDocumentIngestionService ingestion,
        IRagDocumentQueryService documents,
        IMediator mediator)
    {
        _ingestion = ingestion;
        _documents = documents;
        _mediator = mediator;
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

    /// <summary>List RAG documents (global + per-customer) for the Knowledge Base admin screen.</summary>
    [HttpGet("documents")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RagDocumentResponse>>>> GetDocuments(
        CancellationToken cancellationToken)
    {
        var documents = await _documents.GetDocumentsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RagDocumentResponse>>.Ok(documents));
    }

    /// <summary>Lists every AI feature's editable prompt settings for the admin "AI config" screen.</summary>
    [HttpGet("prompt-configs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AiPromptConfigDto>>>> GetPromptConfigs(
        CancellationToken cancellationToken)
    {
        var configs = await _mediator.Send(new GetAiPromptConfigsQuery(), cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AiPromptConfigDto>>.Ok(configs));
    }

    /// <summary>Updates one feature's persona/temperature/token cap; the safety core stays fixed in code.</summary>
    [HttpPut("prompt-configs/{featureKey}")]
    public async Task<ActionResult<ApiResponse<AiPromptConfigDto>>> UpdatePromptConfig(
        string featureKey,
        [FromBody] UpdateAiPromptConfigRequest request,
        CancellationToken cancellationToken)
    {
        var config = await _mediator.Send(
            new UpdateAiPromptConfigCommand(featureKey, GetAdminId(), request),
            cancellationToken);
        return Ok(ApiResponse<AiPromptConfigDto>.Ok(config, "Đã cập nhật cấu hình AI."));
    }

    /// <summary>Change timeline (newest first) for one feature's prompt config, for audit/revert.</summary>
    [HttpGet("prompt-configs/{featureKey}/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AiPromptConfigHistoryDto>>>> GetPromptConfigHistory(
        string featureKey,
        CancellationToken cancellationToken)
    {
        var history = await _mediator.Send(
            new GetAiPromptConfigHistoryQuery(featureKey),
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AiPromptConfigHistoryDto>>.Ok(history));
    }

    private Guid GetAdminId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(claimValue, out var adminId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid admin identifier claim.");

        return adminId;
    }
}
