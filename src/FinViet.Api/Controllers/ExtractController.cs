using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Transactions;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Route("api/extract")]
[Authorize(Roles = "Customer")]
public class ExtractController : ControllerBase
{
    private readonly ITransactionExtractService _extract;

    public ExtractController(ITransactionExtractService extract)
    {
        _extract = extract;
    }

    public class SmsExtractRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class CsvExtractFormRequest
    {
        public IFormFile File { get; set; } = null!;
        public int? MaxRows { get; set; }
    }

    // POST /api/extract/sms — parse pasted SMS text → candidate rows + AI category suggestions
    [HttpPost("sms")]
    public async Task<ActionResult<ApiResponse<ExtractResponse>>> ExtractSms(
        [FromBody] SmsExtractRequest request, CancellationToken cancellationToken)
    {
        var result = await _extract.ExtractSmsAsync(request.Text, cancellationToken);
        return Ok(ApiResponse<ExtractResponse>.Ok(result, "SMS extracted successfully"));
    }

    // POST /api/extract/csv — parse a bank statement file → candidate rows + AI category suggestions
    [HttpPost("csv")]
    public async Task<ActionResult<ApiResponse<ExtractResponse>>> ExtractCsv(
        [FromForm] CsvExtractFormRequest request, CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponse<ExtractResponse>.Fail("File is required."));

        await using var stream = request.File.OpenReadStream();
        var result = await _extract.ExtractCsvAsync(stream, request.MaxRows, cancellationToken);
        return Ok(ApiResponse<ExtractResponse>.Ok(result, "File extracted successfully"));
    }
}
