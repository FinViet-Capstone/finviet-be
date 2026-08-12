namespace FinViet.Infrastructure.Persistence.Entities;

public class AiChatSession
{
    public Guid SessionId { get; set; }

    public Guid CustomerId { get; set; }

    public string Title { get; set; } = "Cuộc trò chuyện mới";

    public bool HistoryEnabled { get; set; } = true;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
