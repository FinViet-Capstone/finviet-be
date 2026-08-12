namespace FinViet.Application.DTOs.Ai;

public class ChatMessageResponse
{
    public Guid MessageId { get; set; }
    public Guid SessionId { get; set; }
    public string SenderType { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime? Timestamp { get; set; }
    public string? DataPeriod { get; set; }
    public IReadOnlyList<ChatCitation> Citations { get; set; } = [];
    public IReadOnlyList<string> Limitations { get; set; } = [];
}

public class ChatAskRequest
{
    public Guid? SessionId { get; set; }
    public string Question { get; set; } = null!;
}

public record ChatCitation(
    string SourceType,
    string Label,
    string? Period = null,
    double? Similarity = null);

public record ChatSessionResponse(
    Guid SessionId,
    string Title,
    bool HistoryEnabled,
    bool IsDefault,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastMessageAt);

public record CreateChatSessionRequest(
    string? Title = null,
    bool? HistoryEnabled = null);

public record UpdateChatSessionRequest(
    string? Title = null,
    bool? HistoryEnabled = null);
