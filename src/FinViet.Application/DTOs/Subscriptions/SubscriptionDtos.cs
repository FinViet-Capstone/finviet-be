namespace FinViet.Application.DTOs.Subscriptions;

public sealed record SubscriptionPaymentStatusDto(long OrderCode, string Status, decimal Amount, Guid? SubscriptionId);

public sealed record ConfirmPayOSWebhookResultDto(string WebhookUrl, string AccountName, string AccountNumber);

public sealed class CreatePaymentResultDto
{
    public long OrderCode { get; init; }
    public string QrCode { get; init; } = null!;
    public decimal Amount { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
