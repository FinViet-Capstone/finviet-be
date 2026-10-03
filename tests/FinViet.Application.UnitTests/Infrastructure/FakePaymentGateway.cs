using FinViet.Application.Common.Exceptions;
using FinViet.Application.Interfaces;

namespace FinViet.Application.UnitTests.Infrastructure;

/// <summary>Configurable <see cref="IPaymentGateway"/> double for subscription/payOS tests.</summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly string _qrCode;
    private readonly string _checkoutUrl;
    private readonly WebhookVerificationResult? _webhookResult;
    private readonly PaymentStatusResult? _orderStatusResult;
    private readonly Exception? _orderStatusException;
    private readonly ConfirmWebhookResult? _confirmWebhookResult;
    private readonly Exception? _confirmWebhookException;

    public FakePaymentGateway(string qrCode = "", string checkoutUrl = "",
        WebhookVerificationResult? webhookResult = null,
        PaymentStatusResult? orderStatusResult = null,
        Exception? orderStatusException = null,
        ConfirmWebhookResult? confirmWebhookResult = null,
        Exception? confirmWebhookException = null)
    {
        _qrCode = qrCode;
        _checkoutUrl = checkoutUrl;
        _webhookResult = webhookResult;
        _orderStatusResult = orderStatusResult;
        _orderStatusException = orderStatusException;
        _confirmWebhookResult = confirmWebhookResult;
        _confirmWebhookException = confirmWebhookException;
    }

    public Task<CreateOrderResult> CreateOrderAsync(
        long orderCode, int amount, string description, DateTimeOffset expiry,
        string returnUrl, string cancelUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CreateOrderResult(_qrCode, _checkoutUrl));

    public Task<WebhookVerificationResult> VerifyWebhookAsync(
        string webhookBody, CancellationToken cancellationToken = default) =>
        Task.FromResult(_webhookResult ?? throw new BadRequestException("No webhook result configured."));

    public Task<PaymentStatusResult> GetOrderStatusAsync(
        long orderCode, CancellationToken cancellationToken = default) =>
        _orderStatusException is not null
            ? Task.FromException<PaymentStatusResult>(_orderStatusException)
            : Task.FromResult(_orderStatusResult ?? throw new BadRequestException("No order status configured."));

    public Task<ConfirmWebhookResult> ConfirmWebhookAsync(
        string webhookUrl, CancellationToken cancellationToken = default) =>
        _confirmWebhookException is not null
            ? Task.FromException<ConfirmWebhookResult>(_confirmWebhookException)
            : Task.FromResult(_confirmWebhookResult ?? throw new BadRequestException("No confirm-webhook result configured."));
}
