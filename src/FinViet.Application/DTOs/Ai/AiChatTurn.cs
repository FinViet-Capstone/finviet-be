namespace FinViet.Application.DTOs.Ai;

/// <summary>A single conversation turn passed to the chatbot for context.</summary>
public class AiChatTurn
{
    /// <summary>"USER" or "AI".</summary>
    public string SenderType { get; set; } = null!;

    public string Content { get; set; } = null!;
}
