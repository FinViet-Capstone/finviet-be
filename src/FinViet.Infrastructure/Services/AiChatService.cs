using System.Text;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Services;

public class AiChatService : IAiChatService
{
    private const int RecentTurnsForPrompt = 6;
    private const string SenderUser = "USER";
    private const string SenderAi = "AI";

    private readonly FinVietDbContext _db;
    private readonly IGeminiClient _gemini;
    private readonly ISpendingScoreService _scoreService;

    public AiChatService(FinVietDbContext db, IGeminiClient gemini, ISpendingScoreService scoreService)
    {
        _db = db;
        _gemini = gemini;
        _scoreService = scoreService;
    }

    public async Task<ChatMessageResponse> AskAsync(
        Guid customerId, string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new BadRequestException("Câu hỏi không được để trống.");

        // Persist the user turn first.
        var userMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            CustomerId = customerId,
            SenderType = SenderUser,
            Content = question.Trim(),
            Timestamps = DateTime.UtcNow
        };
        _db.ChatMessages.Add(userMsg);
        await _db.SaveChangesAsync(cancellationToken);

        var context = await BuildContextAsync(customerId, cancellationToken);
        var recentTurns = await RecentTurnsAsync(customerId, cancellationToken);

        string answer;
        try
        {
            answer = await _gemini.ChatAsync(context, recentTurns, question.Trim(), cancellationToken);
        }
        catch (GeminiUnavailableException)
        {
            answer = "Xin lỗi, trợ lý AI hiện chưa sẵn sàng. Bạn vui lòng thử lại sau ít phút.";
        }

        var aiMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            CustomerId = customerId,
            SenderType = SenderAi,
            Content = answer,
            Timestamps = DateTime.UtcNow
        };
        _db.ChatMessages.Add(aiMsg);
        await _db.SaveChangesAsync(cancellationToken);

        return new ChatMessageResponse
        {
            MessageId = aiMsg.MessageId,
            SenderType = SenderAi,
            Content = answer,
            Timestamp = aiMsg.Timestamps
        };
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid customerId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ChatMessages
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.Timestamps)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(m => m.Timestamps)
            .Select(m => new ChatMessageResponse
            {
                MessageId = m.MessageId,
                SenderType = m.SenderType,
                Content = m.Content,
                Timestamp = m.Timestamps
            })
            .ToList();
    }

    private async Task<IReadOnlyList<AiChatTurn>> RecentTurnsAsync(Guid customerId, CancellationToken ct)
    {
        var rows = await _db.ChatMessages
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.Timestamps)
            .Take(RecentTurnsForPrompt)
            .ToListAsync(ct);

        return rows
            .OrderBy(m => m.Timestamps)
            .Select(m => new AiChatTurn { SenderType = m.SenderType, Content = m.Content })
            .ToList();
    }

    /// <summary>Aggregated financial summary for the current month — never raw transactions.</summary>
    private async Task<string> BuildContextAsync(Guid customerId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = DateRange.StartUtc(new DateOnly(today.Year, today.Month, 1));
        var monthEnd = DateRange.EndUtc(today);

        var income = await _db.Customers
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.MonthlyIncomeExpected)
            .FirstOrDefaultAsync(ct);

        var totalBalance = await _db.Wallets
            .Where(w => w.CustomerId == customerId)
            .SumAsync(w => (decimal?)w.Balance, ct) ?? 0m;

        var spendByCategory = await _db.Transactions
            .Join(_db.Wallets, t => t.WalletId, w => w.WalletId, (t, w) => new { t, w.CustomerId })
            .Where(x => x.CustomerId == customerId
                        && x.t.TransactionType == "EXPENSE"
                        && x.t.TransactionDate >= monthStart && x.t.TransactionDate <= monthEnd)
            .Join(_db.Categories, x => x.t.CategoryId, c => c.CategoryId,
                (x, c) => new { c.CategoryName, c.DefaultBucket, x.t.Amount })
            .GroupBy(x => new { x.CategoryName, x.DefaultBucket })
            .Select(g => new { g.Key.CategoryName, g.Key.DefaultBucket, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var totalSpent = spendByCategory.Sum(s => s.Total);
        var byBucket = spendByCategory
            .GroupBy(s => s.DefaultBucket ?? "Khác")
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        var sb = new StringBuilder();
        sb.AppendLine($"Tháng hiện tại: {today:MM/yyyy}.");
        sb.AppendLine($"Thu nhập kỳ vọng hàng tháng: {(income.HasValue ? $"{income.Value:N0}đ" : "chưa thiết lập")}.");
        sb.AppendLine($"Tổng số dư tất cả ví: {totalBalance:N0}đ.");
        sb.AppendLine($"Tổng chi tiêu tháng này: {totalSpent:N0}đ.");
        sb.AppendLine("Chi tiêu theo nhóm:");
        foreach (var b in byBucket.OrderByDescending(x => x.Value))
            sb.AppendLine($"- {b.Key}: {b.Value:N0}đ");
        sb.AppendLine("Chi tiêu theo danh mục:");
        foreach (var s in spendByCategory.OrderByDescending(x => x.Total).Take(8))
            sb.AppendLine($"- {s.CategoryName}: {s.Total:N0}đ");

        try
        {
            var score = await _scoreService.ComputeCurrentAsync(customerId, "MONTHLY", ct);
            sb.AppendLine($"Điểm quản lý chi tiêu hiện tại: {score.FinalScore:0}/100 ({score.ColorBadge}).");
        }
        catch
        {
            // Score is best-effort context; ignore failures.
        }

        return sb.ToString();
    }
}
