namespace FinViet.Infrastructure.ExternalServices.Gemini;

/// <summary>Binds the "Gemini" section of appsettings.json.</summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public string ClassifyModel { get; set; } = "gemini-1.5-flash";

    public string ReportModel { get; set; } = "gemini-1.5-flash";

    public int TimeoutSeconds { get; set; } = 30;
}
