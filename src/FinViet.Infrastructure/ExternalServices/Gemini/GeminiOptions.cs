namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>Binds the "Gemini" section of appsettings.json.</summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public string ClassifyModel { get; set; } = "gemini-2.5-flash";

    public string ReportModel { get; set; } = "gemini-2.5-flash";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>Matryoshka output dimension requested from the embedding model. Must match the
    /// pgvector column dimension (rag_chunk.embedding = vector(768)).</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    public int TimeoutSeconds { get; set; } = 30;
}
