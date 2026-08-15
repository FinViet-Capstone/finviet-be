namespace FinViet.Application.DTOs.Ai;

public class RagDocumentResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? Uri { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ChunkCount { get; set; }
}
