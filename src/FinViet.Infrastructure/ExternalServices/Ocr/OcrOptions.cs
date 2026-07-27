namespace FinViet.Infrastructure.ExternalServices.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    /// <summary>Which provider to call, e.g. "google-vision" or "azure-vision". Empty disables OCR
    /// (photo extraction returns 503 until this is set and a matching provider implementation is
    /// registered in place of <see cref="UnconfiguredReceiptOcrService"/>).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Provider API key / credential. Never checked in — user-secrets or env var only.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Provider endpoint override (e.g. an Azure resource endpoint). Empty uses the
    /// provider SDK's default.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
