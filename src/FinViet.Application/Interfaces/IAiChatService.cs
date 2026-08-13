using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IAiChatService
{
    Task<ChatMessageResponse> AskAsync(
        Guid customerId,
        Guid? sessionId,
        string question,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid customerId,
        Guid? sessionId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<ChatSessionResponse> CreateSessionAsync(
        Guid customerId,
        string? title,
        bool? historyEnabled,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatSessionResponse>> GetSessionsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<ChatSessionResponse> UpdateSessionAsync(
        Guid customerId,
        Guid sessionId,
        string? title,
        bool? historyEnabled,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(
        Guid customerId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
