namespace FinViet.Application.Interfaces;

public interface IPaymentGateway
{
    Task<CreateOrderResult> CreateOrderAsync(
        long orderCode,
        int amount,
        string description,
        DateTimeOffset expiry,
        string returnUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default);

    Task<WebhookVerificationResult> VerifyWebhookAsync(
        string webhookBody,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> GetOrderStatusAsync(
        long orderCode,
        CancellationToken cancellationToken = default);
}

public sealed record CreateOrderResult(string QrCode, string CheckoutUrl);

public sealed record WebhookVerificationResult(
    long OrderCode,
    int Amount,
    bool Success,
    string? TransactionId);

public enum PaymentGatewayStatus
{
    Pending,
    Succeeded,
    Failed,
}

public sealed record PaymentStatusResult(PaymentGatewayStatus Status, string? TransactionId);
