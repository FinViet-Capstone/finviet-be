namespace FinViet.Application.DTOs.Ai;

/// <summary>A single retrieved RAG chunk plus its similarity to the query.</summary>
public class RagHit
{
    public string Content { get; set; } = string.Empty;

    /// <summary>Source type of the parent document: 'pdf' | 'weekly_report' | 'monthly_summary'.</summary>
    public string SourceType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Cosine similarity in [0,1] (1 - cosine distance). Higher is closer.</summary>
    public double Score { get; set; }
}
