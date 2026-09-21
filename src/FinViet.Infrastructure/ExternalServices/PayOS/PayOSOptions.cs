namespace FinViet.Infrastructure.ExternalServices.PayOS;

public sealed class PayOSOptions
{
    public const string SectionName = "PayOS";

    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
}
