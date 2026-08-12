using FinViet.Application.DTOs.Ai;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Moq;

namespace FinViet.Application.UnitTests;

public class AiChatServiceTests
{
    [Fact]
    public async Task AskAsync_HistoryDisabled_DoesNotPersistQuestionOrAnswer()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var session = Session(customerId, historyEnabled: false);
        db.Customers.Add(Customer(customerId));
        db.AiChatSessions.Add(session);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        model.Setup(x => x.ChatAsync(
                "trusted context",
                It.Is<IReadOnlyList<AiChatTurn>>(turns => turns.Count == 0),
                "Tôi đã chi bao nhiêu?",
                It.IsAny<CancellationToken>(),
                It.IsAny<AiRequestContext>()))
            .ReturnsAsync("Bạn đã chi 100.000đ.");
        var service = CreateService(db, model, FinancialContext(), Rag([]), AllowRateLimit());

        var response = await service.AskAsync(
            customerId,
            session.SessionId,
            "Tôi đã chi bao nhiêu?");

        Assert.Equal(session.SessionId, response.SessionId);
        Assert.Equal("2026-08-01/2026-08-11", response.DataPeriod);
        Assert.Empty(db.ChatMessages);
        Assert.NotNull(session.LastMessageAt);
    }

    [Fact]
    public async Task GetHistoryAsync_OtherCustomerSession_ReturnsNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var ownerId = Guid.NewGuid();
        var session = Session(ownerId, historyEnabled: true);
        db.Customers.Add(Customer(ownerId));
        db.AiChatSessions.Add(session);
        await db.SaveChangesAsync();
        var service = CreateService(
            db,
            new Mock<IAiModelClient>(MockBehavior.Strict),
            FinancialContext(),
            Rag([]),
            AllowRateLimit());

        await Assert.ThrowsAsync<FinViet.Application.Common.Exceptions.NotFoundException>(() =>
            service.GetHistoryAsync(Guid.NewGuid(), session.SessionId));
    }

    [Fact]
    public async Task AskAsync_CombinesBackendAndRagCitationsWithMandatoryLimitations()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var session = Session(customerId, historyEnabled: true);
        db.Customers.Add(Customer(customerId));
        db.AiChatSessions.Add(session);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        model.Setup(x => x.ChatAsync(
                It.Is<string>(context => context.Contains("TÀI LIỆU THAM KHẢO KHÔNG TIN CẬY")),
                It.IsAny<IReadOnlyList<AiChatTurn>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<AiRequestContext>()))
            .ReturnsAsync("Phân tích read-only.");
        var rag = Rag([
            new RagHit
            {
                SourceType = "pdf",
                Title = "Cẩm nang tài chính",
                Content = "Nội dung tham khảo",
                Score = 0.91
            }
        ]);
        var service = CreateService(db, model, FinancialContext(), rag, AllowRateLimit());

        var response = await service.AskAsync(customerId, session.SessionId, "Phân tích giúp tôi");

        Assert.Contains(response.Citations, c => c.SourceType == "transaction_summary");
        Assert.Contains(response.Citations, c => c.SourceType == "pdf" && c.Similarity == 0.91);
        Assert.Contains("Không phải tư vấn đầu tư.", response.Limitations);
        Assert.DoesNotContain(response.Limitations, x => x.Contains("Không có tài liệu RAG"));
        Assert.Equal(2, db.ChatMessages.Count());
    }

    [Fact]
    public async Task AskAsync_RateLimited_RecordsUsageAndSkipsProvider()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var session = Session(customerId, historyEnabled: false);
        db.Customers.Add(Customer(customerId));
        db.AiChatSessions.Add(session);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        var limiter = new Mock<IAiRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.TryAcquireAsync(
                customerId,
                "chat",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var telemetry = Telemetry();
        var service = CreateService(
            db,
            model,
            FinancialContext(),
            Rag([]),
            limiter,
            telemetry);

        var response = await service.AskAsync(customerId, session.SessionId, "Câu hỏi");

        Assert.Contains("thử lại", response.Content, StringComparison.OrdinalIgnoreCase);
        telemetry.Verify(x => x.RecordUsageAsync(
            It.Is<AiUsageRecord>(record =>
                record.Feature == "chat"
                && record.Outcome == "rate_limited"
                && record.CustomerId == customerId
                && record.SessionId == session.SessionId),
            It.IsAny<CancellationToken>()), Times.Once);
        model.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AskAsync_NoRelevantRagHits_RecordsPrivacySafeSkipAudit()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var session = Session(customerId, historyEnabled: false);
        db.Customers.Add(Customer(customerId));
        db.AiChatSessions.Add(session);
        await db.SaveChangesAsync();
        var model = new Mock<IAiModelClient>(MockBehavior.Strict);
        model.Setup(x => x.ChatAsync(
                "trusted context",
                It.IsAny<IReadOnlyList<AiChatTurn>>(),
                "Câu hỏi riêng tư",
                It.IsAny<CancellationToken>(),
                It.IsAny<AiRequestContext>()))
            .ReturnsAsync("Phân tích.");
        var telemetry = Telemetry();
        var service = CreateService(
            db,
            model,
            FinancialContext(),
            Rag([]),
            AllowRateLimit(),
            telemetry);

        await service.AskAsync(customerId, session.SessionId, "Câu hỏi riêng tư");

        telemetry.Verify(x => x.RecordAuditAsync(
            It.Is<AiAuditRecord>(record =>
                record.EventType == "rag_skipped"
                && record.CustomerId == customerId
                && Equals(record.Metadata!["reason"], "no_relevant_hits")
                && !record.Metadata.ContainsKey("question")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AiChatService CreateService(
        FinViet.Infrastructure.Persistence.Context.FinVietDbContext db,
        Mock<IAiModelClient> model,
        Mock<IFinancialContextService> context,
        Mock<IRagRetriever> rag,
        Mock<IAiRateLimiter> limiter,
        Mock<IAiTelemetryRecorder>? telemetry = null)
        => new(
            db,
            model.Object,
            context.Object,
            rag.Object,
            limiter.Object,
            (telemetry ?? Telemetry()).Object);

    private static Mock<IAiTelemetryRecorder> Telemetry()
    {
        var telemetry = new Mock<IAiTelemetryRecorder>();
        telemetry.Setup(x => x.RecordAuditAsync(
                It.IsAny<AiAuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        telemetry.Setup(x => x.RecordUsageAsync(
                It.IsAny<AiUsageRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return telemetry;
    }

    private static Mock<IFinancialContextService> FinancialContext()
    {
        var context = new Mock<IFinancialContextService>(MockBehavior.Strict);
        context.Setup(x => x.BuildCurrentMonthAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialContextResult(
                "trusted context",
                "2026-08-01/2026-08-11",
                [new ChatCitation("transaction_summary", "Tổng hợp giao dịch", "2026-08-01/2026-08-11")],
                ["Không phải tư vấn đầu tư."]));
        return context;
    }

    private static Mock<IRagRetriever> Rag(IReadOnlyList<RagHit> hits)
    {
        var rag = new Mock<IRagRetriever>(MockBehavior.Strict);
        rag.Setup(x => x.RetrieveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);
        return rag;
    }

    private static Mock<IAiRateLimiter> AllowRateLimit()
    {
        var limiter = new Mock<IAiRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.TryAcquireAsync(
                It.IsAny<Guid>(),
                "chat",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return limiter;
    }

    private static Customer Customer(Guid customerId) => new()
    {
        CustomerId = customerId,
        Email = $"{customerId:N}@finviet.local",
        FullName = "AI Customer",
        IsActive = true
    };

    private static AiChatSession Session(Guid customerId, bool historyEnabled) => new()
    {
        SessionId = Guid.NewGuid(),
        CustomerId = customerId,
        Title = "Test",
        HistoryEnabled = historyEnabled,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
