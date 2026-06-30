using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;

namespace FinViet.Infrastructure.Services;

public class WeeklyReportService : IWeeklyReportService
{
    private const string Uncategorized = "Chưa phân loại";

    private readonly FinVietDbContext _db;
    private readonly ISpendingScoreService _scoreService;
    private readonly IGeminiClient _gemini;
    private readonly IAiReportNotifier _notifier;
    private readonly IDocumentIngestionService _ingestion;
    private readonly ILogger<WeeklyReportService> _logger;

    public WeeklyReportService(
        FinVietDbContext db,
        ISpendingScoreService scoreService,
        IGeminiClient gemini,
        IAiReportNotifier notifier,
        IDocumentIngestionService ingestion,
        ILogger<WeeklyReportService> logger)
    {
        _db = db;
        _scoreService = scoreService;
        _gemini = gemini;
        _notifier = notifier;
        _ingestion = ingestion;
        _logger = logger;
    }

    public async Task<WeeklyReportResponse> GenerateForWeekAsync(
        Guid customerId, DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default)
    {
        // Idempotent: return existing report for this week if present.
        var existing = await _db.AiWeeklyReports
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.WeekStart == weekStart, cancellationToken);
        if (existing is not null)
            return await ToResponseAsync(existing, cancellationToken);

        // 1. Compute + snapshot the weekly score (persist true).
        var score = await _scoreService.ComputeAsync(
            customerId, "WEEKLY", weekStart, weekEnd, persist: true, includeComment: true, cancellationToken);

        // 2. Build the report context + generate narrative.
        var context = await BuildReportContextAsync(customerId, weekStart, weekEnd, score, cancellationToken);
        string narrative;
        try
        {
            narrative = await _gemini.GenerateReportAsync(context, cancellationToken);
        }
        catch (GeminiUnavailableException ex)
        {
            _logger.LogWarning(ex, "Weekly report narrative generation failed for {CustomerId}.", customerId);
            // Fallback narrative so a report still exists; the snapshot score remains valid.
            narrative = BuildFallbackNarrative(score);
        }

        // 3. Persist (guard against a concurrent insert via the unique index).
        var report = new AiWeeklyReport
        {
            ReportId = Guid.NewGuid(),
            CustomerId = customerId,
            WeekStart = weekStart,
            Narrative = narrative,
            IsRead = false,
            GeneratedAt = DateTime.UtcNow
        };
        _db.AiWeeklyReports.Add(report);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race — another run inserted it. Return the winner.
            _db.Entry(report).State = EntityState.Detached;
            var winner = await _db.AiWeeklyReports
                .FirstAsync(r => r.CustomerId == customerId && r.WeekStart == weekStart, cancellationToken);
            return await ToResponseAsync(winner, cancellationToken);
        }

        // 4. Push notification (never let push failure break generation).
        await _notifier.SendReportReadyAsync(
            customerId,
            "Báo cáo tài chính tuần đã sẵn sàng",
            $"Điểm ví tuần này: {score.FinalScore:0}/100. Xem chi tiết trong ứng dụng.",
            cancellationToken);

        // 5. Index the narrative into the customer's personal RAG corpus (best-effort).
        try
        {
            await _ingestion.IndexWeeklyReportAsync(report.ReportId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to index weekly report {ReportId} into RAG corpus.", report.ReportId);
        }

        return await ToResponseAsync(report, cancellationToken);
    }

    public async Task<IReadOnlyList<WeeklyReportResponse>> GetHistoryAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var reports = await _db.AiWeeklyReports
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.WeekStart)
            .ToListAsync(cancellationToken);

        var result = new List<WeeklyReportResponse>(reports.Count);
        foreach (var r in reports)
            result.Add(await ToResponseAsync(r, cancellationToken));
        return result;
    }

    public async Task<WeeklyReportResponse?> GetByIdAsync(
        Guid customerId, Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.AiWeeklyReports
            .FirstOrDefaultAsync(r => r.ReportId == reportId && r.CustomerId == customerId, cancellationToken);
        return report is null ? null : await ToResponseAsync(report, cancellationToken);
    }

    private async Task<string> BuildReportContextAsync(
        Guid customerId, DateOnly start, DateOnly end, SpendingScoreResult score, CancellationToken ct)
    {
        var startDt = DateRange.StartUtc(start);
        var endDt = DateRange.EndUtc(end);

        var spendByCategory = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.t.TransactionType == "EXPENSE"
                        && x.t.TransactionDate >= startDt && x.t.TransactionDate <= endDt)
            .Join(_db.Categories, x => x.t.CategoryId, c => c.CategoryId, (x, c) => new { c.CategoryName, x.t.Amount })
            .GroupBy(x => x.CategoryName)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Total)
            .ToListAsync(ct);

        var totalSpent = spendByCategory.Sum(s => s.Total);
        var lines = spendByCategory.Take(8).Select(s => $"- {s.Category}: {s.Total:N0}đ");

        return
            $"Tuần {start:dd/MM} - {end:dd/MM/yyyy}.\n" +
            $"Tổng chi tiêu: {totalSpent:N0}đ.\n" +
            $"Điểm ví: {score.FinalScore:0}/100 ({score.ColorBadge}).\n" +
            $"Chi tiết theo danh mục:\n{string.Join("\n", lines)}";
    }

    private static string BuildFallbackNarrative(SpendingScoreResult score)
        => $"Tuần này điểm quản lý chi tiêu của bạn là {score.FinalScore:0}/100. " +
           "Hiện chưa thể tạo nhận xét chi tiết, bạn hãy xem lại các danh mục chi tiêu trong ứng dụng " +
           "và cân nhắc đặt hạn mức cho những khoản vượt ngân sách.";

    private async Task<WeeklyReportResponse> ToResponseAsync(AiWeeklyReport r, CancellationToken ct)
    {
        // v3 has no report→score link; fetch the matching weekly snapshot by (customer, week_start).
        var snapshot = await _db.AiSpendingScores
            .Where(s => s.CustomerId == r.CustomerId && s.View == "weekly" && s.PeriodStart == r.WeekStart)
            .Select(s => new { s.Score, s.Color })
            .FirstOrDefaultAsync(ct);

        return new WeeklyReportResponse
        {
            ReportId = r.ReportId,
            PeriodStart = r.WeekStart,
            PeriodEnd = r.WeekStart.AddDays(6),
            Narrative = r.Narrative,
            FinalScore = snapshot is null ? null : snapshot.Score,
            ColorBadge = snapshot?.Color,
            GeneratedAt = r.GeneratedAt
        };
    }
}
