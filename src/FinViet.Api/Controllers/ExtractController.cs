using FinViet.Application.Common;
using FinViet.Application.DTOs;
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

    private const long MaxCsvFileBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxSmsTextLength = 20_000;
    private static readonly string[] AllowedCsvExtensions = { ".csv", ".xlsx", ".xls" };

    // POST /api/extract/sms — parse pasted SMS text → candidate rows + AI category suggestions
    [HttpPost("sms")]
    public async Task<ActionResult<ApiResponse<ExtractResponse>>> ExtractSms(
        [FromBody] SmsExtractRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(ApiResponse<ExtractResponse>.Fail("Vui lòng dán nội dung tin nhắn cần trích xuất."));

        if (request.Text.Length > MaxSmsTextLength)
            return BadRequest(ApiResponse<ExtractResponse>.Fail(
                $"Nội dung quá dài (tối đa {MaxSmsTextLength:N0} ký tự). Hãy chia nhỏ và dán lại."));

        var result = await _extract.ExtractSmsAsync(request.Text, cancellationToken);
        return Ok(ApiResponse<ExtractResponse>.Ok(result, BuildSmsResultMessage(result)));
    }

    /// <summary>Builds a message that tells the user exactly what happened and, when nothing was
    /// recognized, what a valid SMS should look like — so they know what to paste.</summary>
    private static string BuildSmsResultMessage(ExtractResponse result)
    {
        if (result.Rows.Count == 0)
            return "Không nhận diện được giao dịch nào. Hãy dán nguyên văn tin nhắn biến động số dư " +
                   "từ ngân hàng. Mỗi tin cần có số tiền kèm đơn vị (VND/VNĐ/đ), ví dụ: " +
                   "\"TK 0123 -350,000 VND luc 12/06/2025 12:30. ND: thanh toan ca phe\". " +
                   "Nếu dán nhiều tin một lúc, hãy cách nhau bằng một dòng trống.";

        var message = $"Đã nhận diện {result.Rows.Count} giao dịch từ tin nhắn.";
        if (result.Skipped > 0)
            message += $" Bỏ qua {result.Skipped} tin không đọc được số tiền (xem chi tiết ở mục errors).";
        return message;
    }

    // POST /api/extract/csv — parse a bank statement file → candidate rows + AI category suggestions
    [HttpPost("csv")]
    public async Task<ActionResult<ApiResponse<ExtractResponse>>> ExtractCsv(
        [FromForm] CsvExtractFormRequest request, CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponse<ExtractResponse>.Fail("Vui lòng chọn tệp sao kê (.csv, .xlsx) để trích xuất."));

        if (request.File.Length > MaxCsvFileBytes)
            return BadRequest(ApiResponse<ExtractResponse>.Fail(
                $"Tệp quá lớn (tối đa {MaxCsvFileBytes / (1024 * 1024)} MB)."));

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (!AllowedCsvExtensions.Contains(extension))
            return BadRequest(ApiResponse<ExtractResponse>.Fail(
                $"Định dạng tệp không hợp lệ. Chỉ chấp nhận: {string.Join(", ", AllowedCsvExtensions)}."));

        if (request.MaxRows is < 1)
            return BadRequest(ApiResponse<ExtractResponse>.Fail("maxRows phải lớn hơn 0."));

        await using var stream = request.File.OpenReadStream();
        var result = await _extract.ExtractCsvAsync(stream, request.MaxRows, cancellationToken);
        return Ok(ApiResponse<ExtractResponse>.Ok(result, "File extracted successfully"));
    }
}
