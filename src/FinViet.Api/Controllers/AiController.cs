using FinViet.Api.Common;
using FinViet.Application.Common;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinViet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IAiCategorizationService _categorization;
    private readonly IBeneficiaryRuleService _rules;
    private readonly ISpendingScoreService _score;
    private readonly IWeeklyReportService _reports;
    private readonly IAiChatService _chat;

    public AiController(
        IAiCategorizationService categorization,
        IBeneficiaryRuleService rules,
        ISpendingScoreService score,
        IWeeklyReportService reports,
        IAiChatService chat)
    {
        _categorization = categorization;
        _rules = rules;
        _score = score;
        _reports = reports;
        _chat = chat;
    }

    // ── Categorization ───────────────────────────────────────────────────────────────
    [HttpPost("categorize/preview")]
    public async Task<ActionResult<ApiResponse<AiClassificationResult>>> Preview(
        [FromBody] CategorizePreviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _categorization.PreviewAsync(request.Input, cancellationToken);
        return Ok(ApiResponse<AiClassificationResult>.Ok(result, "Phân loại gợi ý thành công."));
    }

    [HttpPost("categorize/{transactionId:guid}")]
    public async Task<ActionResult<ApiResponse<CategorizationOutcome>>> Categorize(
        [FromRoute] Guid transactionId, CancellationToken cancellationToken)
    {
        var outcome = await _categorization.CategorizeTransactionAsync(transactionId, cancellationToken);
        return Ok(ApiResponse<CategorizationOutcome>.Ok(outcome, "Phân loại giao dịch hoàn tất."));
    }

    [HttpPost("transactions/{transactionId:guid}/override")]
    public async Task<ActionResult<ApiResponse<CategorizationOutcome>>> Override(
        [FromRoute] Guid transactionId,
        [FromBody] OverrideCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var outcome = await _rules.OverrideCategoryAsync(customerId, transactionId, request, cancellationToken);
        return Ok(ApiResponse<CategorizationOutcome>.Ok(outcome, "Cập nhật danh mục thành công."));
    }

    // ── Spending score ───────────────────────────────────────────────────────────────
    [HttpGet("score")]
    public async Task<ActionResult<ApiResponse<SpendingScoreResult>>> GetScore(
        [FromQuery] string period = "WEEKLY", CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var normalized = period.ToUpperInvariant() == "MONTHLY" ? "MONTHLY" : "WEEKLY";
        var result = await _score.ComputeCurrentAsync(customerId, normalized, cancellationToken);
        return Ok(ApiResponse<SpendingScoreResult>.Ok(result, "Điểm chi tiêu hiện tại."));
    }

    // ── Weekly reports ───────────────────────────────────────────────────────────────
    [HttpGet("reports")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WeeklyReportResponse>>>> GetReports(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var reports = await _reports.GetHistoryAsync(customerId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WeeklyReportResponse>>.Ok(reports, "Lịch sử báo cáo tuần."));
    }

    [HttpGet("reports/{reportId:guid}")]
    public async Task<ActionResult<ApiResponse<WeeklyReportResponse>>> GetReport(
        [FromRoute] Guid reportId, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var report = await _reports.GetByIdAsync(customerId, reportId, cancellationToken);
        if (report is null)
            return NotFound(ApiResponse<WeeklyReportResponse>.Fail("Không tìm thấy báo cáo."));
        return Ok(ApiResponse<WeeklyReportResponse>.Ok(report, "Chi tiết báo cáo tuần."));
    }

    /// <summary>Manually trigger generation of the most recent completed week's report (for the
    /// current user). Useful for testing the weekly flow without waiting for the Monday job.</summary>
    [HttpPost("reports/generate")]
    public async Task<ActionResult<ApiResponse<WeeklyReportResponse>>> GenerateReport(
        CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int diff = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-diff);
        var weekStart = thisMonday.AddDays(-7);
        var weekEnd = thisMonday.AddDays(-1);

        var report = await _reports.GenerateForWeekAsync(customerId, weekStart, weekEnd, cancellationToken);
        return Ok(ApiResponse<WeeklyReportResponse>.Ok(report, "Đã tạo báo cáo tuần."));
    }

    // ── Chatbot ──────────────────────────────────────────────────────────────────────
    [HttpPost("chat")]
    public async Task<ActionResult<ApiResponse<ChatMessageResponse>>> Chat(
        [FromBody] ChatAskRequest request, CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var reply = await _chat.AskAsync(customerId, request.Question, cancellationToken);
        return Ok(ApiResponse<ChatMessageResponse>.Ok(reply, "Phản hồi từ trợ lý."));
    }

    [HttpGet("chat/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ChatMessageResponse>>>> GetChatHistory(
        [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var history = await _chat.GetHistoryAsync(customerId, limit, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ChatMessageResponse>>.Ok(history, "Lịch sử hội thoại."));
    }
}
