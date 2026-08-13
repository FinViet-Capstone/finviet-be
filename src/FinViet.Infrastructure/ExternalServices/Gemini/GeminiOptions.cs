namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>Binds Gemini Developer API settings. The API key must come from user-secrets or environment variables.</summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";
    public const int RequiredEmbeddingDimensions = 768;

    public string ApiKey { get; set; } = string.Empty;

    public string FlashModel { get; set; } = "gemini-3.6-flash";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>Must match rag_chunk.embedding = vector(768).</summary>
    public int EmbeddingDimensions { get; set; } = RequiredEmbeddingDimensions;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Keep false until all existing embeddings have been re-indexed with Gemini.</summary>
    public bool RagEnabled { get; set; }

    public double RagMinimumSimilarity { get; set; } = 0.72;
}
