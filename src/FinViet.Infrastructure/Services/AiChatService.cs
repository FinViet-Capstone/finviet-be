using System.Text;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using NotFoundException = FinViet.Application.Common.Exceptions.NotFoundException;

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
    private readonly IAiModelClient _aiModel;
    private readonly IFinancialContextService _financialContext;
    private readonly IRagRetriever _retriever;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly IAiTelemetryRecorder _telemetry;

    public AiChatService(
        FinVietDbContext db,
        IAiModelClient aiModel,
        IFinancialContextService financialContext,
        IRagRetriever retriever,
        IAiRateLimiter rateLimiter,
        IAiTelemetryRecorder telemetry)
    {
        _db = db;
        _aiModel = aiModel;
        _financialContext = financialContext;
        _retriever = retriever;
        _rateLimiter = rateLimiter;
        _telemetry = telemetry;
    }

    public async Task<ChatMessageResponse> AskAsync(
        Guid customerId,
        Guid? sessionId,
        string question,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuestion = question?.Trim() ?? string.Empty;
        if (normalizedQuestion.Length is < 1 or > 2000)
            throw new BadRequestException("Câu hỏi phải có từ 1 đến 2.000 ký tự.");

        var session = await ResolveSessionAsync(customerId, sessionId, cancellationToken);
        if (!await _rateLimiter.TryAcquireAsync(customerId, "chat", cancellationToken))
        {
            await _telemetry.RecordUsageAsync(
                new AiUsageRecord(
                    "chat",
                    "gemini",
                    "rate_limited",
                    customerId,
                    session.SessionId),
                cancellationToken);
            return await CompleteTurnAsync(
                session,
                normalizedQuestion,
                RateLimitedReply,
                cancellationToken);
        }

        var financialContext = await _financialContext.BuildCurrentMonthAsync(customerId, cancellationToken);
        var retrieved = await RetrieveKnowledgeAsync(customerId, normalizedQuestion, cancellationToken);
        var recentTurns = session.HistoryEnabled
            ? await RecentTurnsAsync(customerId, session.SessionId, cancellationToken)
            : [];

        var contextBlock = retrieved.Context.Length == 0
            ? financialContext.Content
            : $"{financialContext.Content}\n=== TÀI LIỆU THAM KHẢO KHÔNG TIN CẬY ===\n{retrieved.Context}";

        string answer;
        try
        {
            answer = await _aiModel.ChatAsync(
                contextBlock,
                recentTurns,
                normalizedQuestion,
                cancellationToken,
                new AiRequestContext("chat", customerId, session.SessionId));
        }
        catch (AiProviderUnavailableException)
        {
            answer = UnavailableReply;
        }

        var response = await CompleteTurnAsync(
            session,
            normalizedQuestion,
            answer,
            cancellationToken);
        response.DataPeriod = financialContext.DataPeriod;
        response.Citations = financialContext.Citations
            .Concat(retrieved.Citations)
            .ToList();
        var limitations = financialContext.Limitations.ToList();
        if (retrieved.Citations.Count == 0)
            limitations.Add("Không có tài liệu RAG đủ liên quan cho câu hỏi này.");
        response.Limitations = limitations;
        return response;
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid customerId,
        Guid? sessionId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var session = await ResolveSessionAsync(customerId, sessionId, cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        if (!session.HistoryEnabled)
            return [];

        var rows = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.CustomerId == customerId && m.SessionId == session.SessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(m => m.CreatedAt)
            .Select(m => ToMessageResponse(m, session.SessionId))
            .ToList();
    }

    public async Task<ChatSessionResponse> CreateSessionAsync(
        Guid customerId,
        string? title,
        bool? historyEnabled,
        CancellationToken cancellationToken = default)
    {
        await EnsureCustomerAsync(customerId, cancellationToken);
        var preferenceDefault = await _db.AiCustomerPreferences.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => (bool?)p.DefaultHistoryEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        var now = DateTime.UtcNow;
        var session = new AiChatSession
        {
            SessionId = Guid.NewGuid(),
            CustomerId = customerId,
            Title = NormalizeTitle(title),
            HistoryEnabled = historyEnabled ?? preferenceDefault,
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AiChatSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        await RecordSessionAuditAsync(
            customerId,
            session.SessionId,
            "chat_session_created",
            cancellationToken);
        return ToSessionResponse(session);
    }

    public async Task<IReadOnlyList<ChatSessionResponse>> GetSessionsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.AiChatSessions.AsNoTracking()
            .Where(s => s.CustomerId == customerId && s.DeletedAt == null)
            .OrderByDescending(s => s.LastMessageAt ?? s.CreatedAt)
            .Select(s => new ChatSessionResponse(
                s.SessionId,
                s.Title,
                s.HistoryEnabled,
                s.IsDefault,
                s.CreatedAt,
                s.UpdatedAt,
                s.LastMessageAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatSessionResponse> UpdateSessionAsync(
        Guid customerId,
        Guid sessionId,
        string? title,
        bool? historyEnabled,
        CancellationToken cancellationToken = default)
    {
        var session = await OwnedSessionAsync(customerId, sessionId, cancellationToken);
        if (title is not null)
            session.Title = NormalizeTitle(title);
        if (historyEnabled.HasValue)
            session.HistoryEnabled = historyEnabled.Value;
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await RecordSessionAuditAsync(
            customerId,
            sessionId,
            "chat_session_updated",
            cancellationToken);
        return ToSessionResponse(session);
    }

    public async Task DeleteSessionAsync(
        Guid customerId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await OwnedSessionAsync(customerId, sessionId, cancellationToken);
        _db.AiChatSessions.Remove(session);
        await _db.SaveChangesAsync(cancellationToken);
        await RecordSessionAuditAsync(
            customerId,
            sessionId,
            "chat_session_deleted",
            cancellationToken);
    }

    private async Task<ChatMessageResponse> CompleteTurnAsync(
        AiChatSession session,
        string question,
        string answer,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var responseId = Guid.NewGuid();
        if (session.HistoryEnabled)
        {
            _db.ChatMessages.AddRange(
                new ChatMessage
                {
                    MessageId = Guid.NewGuid(),
                    CustomerId = session.CustomerId,
                    Role = RoleUser,
                    Content = question,
                    SessionId = session.SessionId,
                    CreatedAt = now
                },
                new ChatMessage
                {
                    MessageId = responseId,
                    CustomerId = session.CustomerId,
                    Role = RoleAssistant,
                    Content = answer,
                    SessionId = session.SessionId,
                    CreatedAt = now
                });
        }

        session.LastMessageAt = now;
        session.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return new ChatMessageResponse
        {
            MessageId = responseId,
            SessionId = session.SessionId,
            SenderType = SenderAi,
            Content = answer,
            Timestamp = now
        };
    }

    private async Task<AiChatSession> ResolveSessionAsync(
        Guid customerId,
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId.HasValue)
            return await OwnedSessionAsync(customerId, sessionId.Value, cancellationToken);

        var existing = await _db.AiChatSessions.FirstOrDefaultAsync(
            s => s.CustomerId == customerId && s.IsDefault && s.DeletedAt == null,
            cancellationToken);
        if (existing is not null)
            return existing;

        await EnsureCustomerAsync(customerId, cancellationToken);
        var historyEnabled = await _db.AiCustomerPreferences.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => (bool?)p.DefaultHistoryEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        var now = DateTime.UtcNow;
        var session = new AiChatSession
        {
            SessionId = customerId,
            CustomerId = customerId,
            Title = "Trò chuyện mặc định",
            HistoryEnabled = historyEnabled,
            IsDefault = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.AiChatSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);
        return session;
    }

    private async Task<AiChatSession> OwnedSessionAsync(
        Guid customerId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await _db.AiChatSessions.FirstOrDefaultAsync(
                   s => s.SessionId == sessionId
                        && s.CustomerId == customerId
                        && s.DeletedAt == null,
                   cancellationToken)
               ?? throw new NotFoundException("Chat session", sessionId);
    }

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await _db.Customers.AsNoTracking().AnyAsync(
                c => c.CustomerId == customerId && c.IsActive,
                cancellationToken))
        {
            throw new NotFoundException("Customer", customerId);
        }
    }

    private async Task<(string Context, IReadOnlyList<ChatCitation> Citations)> RetrieveKnowledgeAsync(
        Guid customerId,
        string question,
        CancellationToken cancellationToken)
    {
        var ragAllowed = await _db.AiCustomerPreferences.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .Select(p => (bool?)p.RagEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!ragAllowed)
        {
            await RecordRagAuditAsync(customerId, "rag_skipped", "customer_disabled", cancellationToken);
            return (string.Empty, []);
        }

        try
        {
            var hits = await _retriever.RetrieveAsync(
                customerId,
                question,
                RetrievedChunks,
                cancellationToken);
            if (hits.Count == 0)
            {
                await RecordRagAuditAsync(customerId, "rag_skipped", "no_relevant_hits", cancellationToken);
                return (string.Empty, []);
            }

            var context = new StringBuilder();
            var citations = new List<ChatCitation>();
            foreach (var hit in hits)
            {
                context.AppendLine($"- ({hit.Title}) {hit.Content}");
                citations.Add(new ChatCitation(hit.SourceType, hit.Title, Similarity: hit.Score));
            }
            return (context.ToString(), citations);
        }
        catch (AiProviderUnavailableException)
        {
            await RecordRagAuditAsync(customerId, "rag_failed", "provider_unavailable", cancellationToken);
            return (string.Empty, []);
        }
    }

    private Task RecordRagAuditAsync(
        Guid customerId,
        string eventType,
        string reason,
        CancellationToken cancellationToken)
        => _telemetry.RecordAuditAsync(
            new AiAuditRecord(
                eventType,
                "system",
                customerId,
                Metadata: new Dictionary<string, object?> { ["reason"] = reason }),
            cancellationToken);

    private async Task<IReadOnlyList<AiChatTurn>> RecentTurnsAsync(
        Guid customerId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.CustomerId == customerId && m.SessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(RecentTurnsForPrompt)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AiChatTurn { SenderType = ToSender(m.Role), Content = m.Content })
            .ToList();
    }

    private static ChatMessageResponse ToMessageResponse(ChatMessage message, Guid sessionId) => new()
    {
        MessageId = message.MessageId,
        SessionId = sessionId,
        SenderType = ToSender(message.Role),
        Content = message.Content,
        Timestamp = message.CreatedAt
    };

    private static string ToSender(string role) =>
        string.Equals(role, RoleAssistant, StringComparison.OrdinalIgnoreCase) ? SenderAi : SenderUser;

    private static string NormalizeTitle(string? title)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "Cuộc trò chuyện mới" : title.Trim();
        if (normalized.Length > 120)
            throw new BadRequestException("Tiêu đề phiên trò chuyện không được vượt quá 120 ký tự.");
        return normalized;
    }

    private static ChatSessionResponse ToSessionResponse(AiChatSession session) => new(
        session.SessionId,
        session.Title,
        session.HistoryEnabled,
        session.IsDefault,
        session.CreatedAt,
        session.UpdatedAt,
        session.LastMessageAt);

    private Task RecordSessionAuditAsync(
        Guid customerId,
        Guid sessionId,
        string eventType,
        CancellationToken cancellationToken)
        => _telemetry.RecordAuditAsync(
            new AiAuditRecord(
                eventType,
                "customer",
                customerId,
                sessionId),
            cancellationToken);

}
