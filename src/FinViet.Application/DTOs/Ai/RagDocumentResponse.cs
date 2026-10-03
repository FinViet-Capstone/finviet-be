namespace FinViet.Application.DTOs.Ai;

public class RagDocumentResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? Uri { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ChunkCount { get; set; }

    /// <summary>False for documents ingested before original files were stored in the database —
    /// their chunks still serve RAG, but the file itself is gone and must be re-uploaded.</summary>
    public bool HasFile { get; set; }
}
