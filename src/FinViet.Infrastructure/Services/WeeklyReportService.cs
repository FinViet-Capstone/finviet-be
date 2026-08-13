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
    private readonly IAiModelClient _aiModel;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly IAiTelemetryRecorder _telemetry;
    private readonly IAiReportNotifier _notifier;
    private readonly IDocumentIngestionService _ingestion;
    private readonly ILogger<WeeklyReportService> _logger;

    public WeeklyReportService(
        FinVietDbContext db,
        ISpendingScoreService scoreService,
        IAiModelClient aiModel,
        IAiRateLimiter rateLimiter,
        IAiTelemetryRecorder telemetry,
        IAiReportNotifier notifier,
        IDocumentIngestionService ingestion,
        ILogger<WeeklyReportService> logger)
    {
        _db = db;
        _scoreService = scoreService;
        _aiModel = aiModel;
        _rateLimiter = rateLimiter;
        _telemetry = telemetry;
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
        if (!await _rateLimiter.TryAcquireAsync(customerId, "weekly_report", cancellationToken))
        {
            narrative = BuildFallbackNarrative(score);
            await _telemetry.RecordUsageAsync(
                new AiUsageRecord(
                    "weekly_report",
                    "gemini",
                    "rate_limited",
                    customerId,
                    Model: null),
                cancellationToken);
            await RecordReportFallbackAsync(customerId, "rate_limited", cancellationToken);
        }
        else
        {
            try
            {
                narrative = await _aiModel.GenerateReportAsync(
                    context,
                    cancellationToken,
                    new AiRequestContext("weekly_report", customerId));
            }
            catch (AiProviderUnavailableException ex)
            {
                _logger.LogWarning(ex, "Weekly report narrative generation failed for {CustomerId}.", customerId);
                // Fallback narrative so a report still exists; the snapshot score remains valid.
                narrative = BuildFallbackNarrative(score);
                await RecordReportFallbackAsync(customerId, "provider_unavailable", cancellationToken);
            }
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

        // 4. Push notification (the notifier enforces the customer's notification preference).
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
                        && x.t.TransactionType == "expense"
                        && x.t.TransactionDate >= startDt && x.t.TransactionDate <= endDt)
            .Join(_db.Categories, x => x.t.CategoryId, c => c.CategoryId, (x, c) => new { c.CategoryName, x.t.Amount })
            .GroupBy(x => x.CategoryName)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Total)
            .ToListAsync(ct);

        var totalSpent = spendByCategory.Sum(s => s.Total);
        var lines = spendByCategory.Take(8).Select(s => $"- {s.Category}: {s.Total:N0}đ");

        var monthKey = start.ToString("yyyy-MM");
        var budgets = await _db.Budgets.AsNoTracking()
            .Where(b => b.CustomerId == customerId)
            .Select(b => new { b.CategoryId, b.MonthlyLimit })
            .ToListAsync(ct);
        var monthStart = DateRange.StartUtc(new DateOnly(start.Year, start.Month, 1));
        var monthEndDate = new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
        var monthEnd = DateRange.EndUtc(monthEndDate);
        var monthSpend = await _db.Transactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId
                        && t.TransactionType == "expense"
                        && t.CategoryId != null
                        && t.TransactionDate >= monthStart
                        && t.TransactionDate <= monthEnd)
            .GroupBy(t => t.CategoryId!)
            .Select(g => new { CategoryId = g.Key, Spent = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Spent, ct);
        var categoryNames = await _db.Categories.AsNoTracking()
            .Where(c => budgets.Select(b => b.CategoryId).Contains(c.CategoryId))
            .ToDictionaryAsync(c => c.CategoryId, c => c.CategoryName, ct);
        var overruns = budgets
            .Select(b => new
            {
                Name = categoryNames.GetValueOrDefault(b.CategoryId, b.CategoryId),
                Overrun = monthSpend.GetValueOrDefault(b.CategoryId) - b.MonthlyLimit
            })
            .Where(x => x.Overrun > 0)
            .OrderByDescending(x => x.Overrun)
            .Select(x => $"- {x.Name}: vượt {x.Overrun:N0}đ")
            .ToList();

        return
            $"Tuần {start:dd/MM} - {end:dd/MM/yyyy}.\n" +
            $"Tổng chi tiêu: {totalSpent:N0}đ.\n" +
            $"Điểm ví: {score.FinalScore:0}/100 ({score.ColorBadge}).\n" +
            $"Chi tiết theo danh mục:\n{string.Join("\n", lines)}\n" +
            $"Vượt ngân sách tháng {monthKey} do backend tính:\n" +
            (overruns.Count == 0 ? "- Không có danh mục vượt ngân sách. " : string.Join("\n", overruns));
    }

    private Task RecordReportFallbackAsync(
        Guid customerId,
        string reason,
        CancellationToken cancellationToken)
        => _telemetry.RecordAuditAsync(
            new AiAuditRecord(
                "weekly_report_fallback",
                "system",
                customerId,
                Metadata: new Dictionary<string, object?> { ["reason"] = reason }),
            cancellationToken);

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
