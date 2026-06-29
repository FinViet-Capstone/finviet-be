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
    private const int RetrievedChunks = 5;
    private const string RoleUser = "user";
    private const string RoleAssistant = "assistant";
    private const string SenderUser = "USER";
    private const string SenderAi = "AI";

    private const string RateLimitedReply =
        "Bạn đã hỏi trợ lý khá nhiều trong thời gian ngắn. Vui lòng thử lại sau ít phút nhé.";
    private const string UnavailableReply =
        "Xin lỗi, trợ lý AI hiện chưa sẵn sàng. Bạn vui lòng thử lại sau ít phút.";

    private readonly FinVietDbContext _db;
    private readonly IGeminiClient _gemini;
    private readonly ISpendingScoreService _scoreService;
    private readonly IRagRetriever _retriever;
    private readonly IAiRateLimiter _rateLimiter;

    public AiChatService(
        FinVietDbContext db,
        IGeminiClient gemini,
        ISpendingScoreService scoreService,
        IRagRetriever retriever,
        IAiRateLimiter rateLimiter)
    {
        _db = db;
        _gemini = gemini;
        _scoreService = scoreService;
        _retriever = retriever;
        _rateLimiter = rateLimiter;
    }

    public async Task<ChatMessageResponse> AskAsync(
        Guid customerId, string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new BadRequestException("Câu hỏi không được để trống.");

        // ai_chat_messages.session_id is required. The API has no session concept yet, so use a
        // stable per-customer session (the customerId) — one continuous thread per customer (MVP).
        var sessionId = customerId;

        // Persist the user turn first.
        var userMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            CustomerId = customerId,
            Role = RoleUser,
            Content = question.Trim(),
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ChatMessages.Add(userMsg);
        await _db.SaveChangesAsync(cancellationToken);

        // In-memory rate limit (replaces the durable ai_usage_log). On limit, persist a
        // friendly reply turn rather than throwing, matching the graceful-fallback pattern.
        if (!_rateLimiter.TryAcquire(customerId, "chat"))
            return await PersistAiReplyAsync(customerId, sessionId, RateLimitedReply, cancellationToken);

        var context = await BuildContextAsync(customerId, cancellationToken);
        var retrieved = await RetrieveKnowledgeAsync(customerId, question.Trim(), cancellationToken);
        var recentTurns = await RecentTurnsAsync(customerId, cancellationToken);

        var contextBlock = string.IsNullOrEmpty(retrieved)
            ? context
            : $"{context}\n=== TÀI LIỆU & BÁO CÁO LIÊN QUAN ===\n{retrieved}";

        string answer;
        try
        {
            answer = await _gemini.ChatAsync(contextBlock, recentTurns, question.Trim(), cancellationToken);
        }
        catch (GeminiUnavailableException)
        {
            answer = UnavailableReply;
        }

        return await PersistAiReplyAsync(customerId, sessionId, answer, cancellationToken);
    }

    private async Task<ChatMessageResponse> PersistAiReplyAsync(
        Guid customerId, Guid sessionId, string answer, CancellationToken ct)
    {
        var aiMsg = new ChatMessage
        {
            MessageId = Guid.NewGuid(),
            CustomerId = customerId,
            Role = RoleAssistant,
            Content = answer,
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ChatMessages.Add(aiMsg);
        await _db.SaveChangesAsync(ct);

        return new ChatMessageResponse
        {
            MessageId = aiMsg.MessageId,
            SenderType = SenderAi,
            Content = answer,
            Timestamp = aiMsg.CreatedAt
        };
    }

    /// <summary>Semantic retrieval over the customer's narratives + global knowledge docs.
    /// Best-effort: if embedding/retrieval is unavailable, returns empty so chat still works
    /// from the deterministic aggregate context alone.</summary>
    private async Task<string> RetrieveKnowledgeAsync(Guid customerId, string question, CancellationToken ct)
    {
        try
        {
            var hits = await _retriever.RetrieveAsync(customerId, question, RetrievedChunks, ct);
            if (hits.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var hit in hits)
                sb.AppendLine($"- ({hit.Title}) {hit.Content}");
            return sb.ToString();
        }
        catch (GeminiUnavailableException)
        {
            return string.Empty;
        }
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid customerId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ChatMessages
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse
            {
                MessageId = m.MessageId,
                SenderType = ToSender(m.Role),
                Content = m.Content,
                Timestamp = m.CreatedAt
            })
            .ToList();
    }

    private async Task<IReadOnlyList<AiChatTurn>> RecentTurnsAsync(Guid customerId, CancellationToken ct)
    {
        var rows = await _db.ChatMessages
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(RecentTurnsForPrompt)
            .ToListAsync(ct);

        return rows
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AiChatTurn { SenderType = ToSender(m.Role), Content = m.Content })
            .ToList();
    }

    /// <summary>Map the v3 chat_role label ("user"/"assistant") to the DTO sender ("USER"/"AI").</summary>
    private static string ToSender(string role) =>
        string.Equals(role, RoleAssistant, StringComparison.OrdinalIgnoreCase) ? SenderAi : SenderUser;

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
