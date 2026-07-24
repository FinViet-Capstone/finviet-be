namespace FinViet.Infrastructure.ExternalServices.OpenAiCompatible;

/// <summary>Binds the provider-neutral "Ai" section of appsettings.json.</summary>
public class AiOptions
{
    public const string SectionName = "Ai";
    public const int RequiredEmbeddingDimensions = 768;

    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string ClassificationModel { get; set; } = "qwen3:4b";

    public string GenerationModel { get; set; } = "qwen3:4b";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Must match rag_chunk.embedding = vector(768).</summary>
    public int EmbeddingDimensions { get; set; } = RequiredEmbeddingDimensions;

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Keep false until all existing embeddings have been re-indexed with the configured model.</summary>
    public bool RagEnabled { get; set; }
}
