using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

public interface IAiChatService
{
    /// <summary>Answer a Vietnamese question using the customer's aggregated financial context and
    /// recent conversation history. Persists both the user message and the AI reply.</summary>
    Task<ChatMessageResponse> AskAsync(
        Guid customerId, string question, CancellationToken cancellationToken = default);

    /// <summary>Return recent conversation history (oldest first) for replay on open.</summary>
    Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid customerId, int limit = 50, CancellationToken cancellationToken = default);
}
