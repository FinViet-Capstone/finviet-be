using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// One chat turn. Maps to the v3 <c>ai_chat_messages</c> table: pg-enum <c>role</c>
/// (user/assistant), required <c>session_id</c>, and <c>created_at</c>.
/// </summary>
public partial class ChatMessage
{
    /// <summary>Maps to column <c>id</c>.</summary>
    public Guid MessageId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>pg enum chat_role: "user" | "assistant".</summary>
    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;

    public Guid SessionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }
}
