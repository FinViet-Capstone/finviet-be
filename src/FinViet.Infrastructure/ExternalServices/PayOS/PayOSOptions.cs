namespace FinViet.Infrastructure.ExternalServices.PayOS;

public sealed class PayOSOptions
{
    public const string SectionName = "PayOS";

    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }

    /// <summary>Browser redirect target after a hosted-checkout payment succeeds. Defaults to AppSettings:FrontendUrl + /payment/return when unset.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Browser redirect target after a hosted-checkout payment is cancelled. Defaults to AppSettings:FrontendUrl + /payment/cancel when unset.</summary>
    public string? CancelUrl { get; set; }
}
