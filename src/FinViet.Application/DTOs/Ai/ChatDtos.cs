namespace FinViet.Application.DTOs.Ai;

public class ChatMessageResponse
{
    public Guid MessageId { get; set; }
    public string SenderType { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime? Timestamp { get; set; }
}

public class ChatAskRequest
{
    public string Question { get; set; } = null!;
}
