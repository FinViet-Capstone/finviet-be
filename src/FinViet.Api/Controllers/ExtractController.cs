using System.Security.Claims;
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
    private readonly IReceiptOcrService _ocr;

    public ExtractController(ITransactionExtractService extract, IReceiptOcrService ocr)
    {
        _extract = extract;
        _ocr = ocr;
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

    public class PhotoExtractFormRequest
    {
        public IFormFile File { get; set; } = null!;
    }

    private const long MaxCsvFileBytes = 5 * 1024 * 1024; // 5 MB
    private const long MaxPhotoFileBytes = 8 * 1024 * 1024; // 8 MB
    private const int MaxSmsTextLength = 20_000;
    private static readonly string[] AllowedCsvExtensions = { ".csv", ".xlsx", ".xls" };
    private static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png", ".heic" };

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

        var result = await _extract.ExtractSmsAsync(GetCustomerId(), request.Text, cancellationToken);
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
        var result = await _extract.ExtractCsvAsync(GetCustomerId(), stream, extension, request.MaxRows, cancellationToken);
        return Ok(ApiResponse<ExtractResponse>.Ok(result, "File extracted successfully"));
    }

    // POST /api/extract/photo — OCR a receipt photo → a single candidate row (same ExtractResponse
    // shape as SMS/CSV so the client reuses the same mapper). No OCR provider is wired in yet
    // (IReceiptOcrService is a placeholder), so this currently always responds 503.
    [HttpPost("photo")]
    public async Task<ActionResult<ApiResponse<ExtractResponse>>> ExtractPhoto(
        [FromForm] PhotoExtractFormRequest request, CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponse<ExtractResponse>.Fail("Vui lòng chọn ảnh hóa đơn để trích xuất."));

        if (request.File.Length > MaxPhotoFileBytes)
            return BadRequest(ApiResponse<ExtractResponse>.Fail(
                $"Ảnh quá lớn (tối đa {MaxPhotoFileBytes / (1024 * 1024)} MB)."));

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (!AllowedPhotoExtensions.Contains(extension))
            return BadRequest(ApiResponse<ExtractResponse>.Fail(
                $"Định dạng ảnh không hợp lệ. Chỉ chấp nhận: {string.Join(", ", AllowedPhotoExtensions)}."));

        await using var stream = request.File.OpenReadStream();
        var row = await _ocr.ExtractAsync(stream, request.File.ContentType, cancellationToken);

        var result = new ExtractResponse
        {
            Rows = row != null ? new List<ExtractedTransactionItem> { row } : new List<ExtractedTransactionItem>(),
            TotalScanned = 1,
            Skipped = row == null ? 1 : 0,
        };

        return Ok(ApiResponse<ExtractResponse>.Ok(result, row != null
            ? "Đã nhận diện hóa đơn."
            : "Không nhận diện được thông tin từ ảnh."));
    }

    private Guid GetCustomerId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claimValue, out var customerId))
            throw new UnauthorizedAccessException("Authenticated user does not have a valid customer identifier claim.");

        return customerId;
    }
}
