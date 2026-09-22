using System.Linq;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using PayOSClient = global::PayOS.PayOSClient;
using PayOSInvalidSignatureException = global::PayOS.Exceptions.InvalidSignatureException;
using PayOSException = global::PayOS.Exceptions.PayOSException;
using CreatePaymentLinkRequest = global::PayOS.Models.V2.PaymentRequests.CreatePaymentLinkRequest;
using PaymentLinkStatus = global::PayOS.Models.V2.PaymentRequests.PaymentLinkStatus;
using Webhook = global::PayOS.Models.Webhooks.Webhook;

namespace FinViet.Infrastructure.ExternalServices.PayOS;

internal sealed class PayOSGateway : IPaymentGateway
{
    private readonly global::PayOS.PayOSClient _client;
    private readonly ILogger<PayOSGateway> _logger;

    public PayOSGateway(global::PayOS.PayOSClient client, ILogger<PayOSGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<CreateOrderResult> CreateOrderAsync(
        long orderCode,
        int amount,
        string description,
        DateTimeOffset expiry,
        string returnUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = amount,
                Description = description,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl,
                ExpiredAt = (int)expiry.ToUnixTimeSeconds(),
            };

            var result = await _client.PaymentRequests.CreateAsync(request);
            return new CreateOrderResult(result.QrCode, result.CheckoutUrl);
        }
        catch (PayOSException ex)
        {
            _logger.LogError(ex, "PayOS CreateOrder failed for orderCode {OrderCode}", orderCode);
            throw new ExternalServiceException(
                "Payment provider failed to create order.", "payos_create_order_failed", ex);
        }
    }

    public async Task<WebhookVerificationResult> VerifyWebhookAsync(
        string webhookBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var webhook = JsonSerializer.Deserialize<Webhook>(webhookBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new BadRequestException("Empty webhook payload.");

            var verified = await _client.Webhooks.VerifyAsync(webhook);

            return new WebhookVerificationResult(
                OrderCode: verified.OrderCode,
                Amount: (int)verified.Amount,
                Success: verified.Code == "00",
                TransactionId: verified.Reference);
        }
        catch (PayOSInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "PayOS webhook signature verification failed");
            throw new BadRequestException("Invalid webhook signature.");
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayOS webhook verification failed unexpectedly");
            throw new BadRequestException("Invalid webhook payload.");
        }
    }

    public async Task<PaymentStatusResult> GetOrderStatusAsync(
        long orderCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var link = await _client.PaymentRequests.GetAsync(
                orderCode,
                new global::PayOS.Models.RequestOptions { CancellationToken = cancellationToken });

            var transactionId = link.Transactions?.FirstOrDefault()?.Reference;

            var status = link.Status switch
            {
                PaymentLinkStatus.Paid => PaymentGatewayStatus.Succeeded,
                PaymentLinkStatus.Cancelled => PaymentGatewayStatus.Failed,
                PaymentLinkStatus.Expired => PaymentGatewayStatus.Failed,
                _ => PaymentGatewayStatus.Pending,
            };

            return new PaymentStatusResult(status, transactionId);
        }
        catch (PayOSException ex)
        {
            _logger.LogError(ex, "PayOS GetOrderStatus failed for orderCode {OrderCode}", orderCode);
            throw new ExternalServiceException(
                "Payment provider failed to fetch order status.", "payos_get_status_failed", ex);
        }
    }

    public async Task<ConfirmWebhookResult> ConfirmWebhookAsync(
        string webhookUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Webhooks.ConfirmAsync(
                webhookUrl,
                new global::PayOS.Models.RequestOptions<global::PayOS.Models.Webhooks.ConfirmWebhookRequest>
                {
                    CancellationToken = cancellationToken,
                });
            return new ConfirmWebhookResult(response.WebhookUrl, response.AccountName, response.AccountNumber);
        }
        catch (PayOSException ex)
        {
            _logger.LogError(ex, "PayOS ConfirmWebhook failed for url {WebhookUrl}", webhookUrl);
            throw new ExternalServiceException(
                "Payment provider rejected the webhook URL.", "payos_confirm_webhook_failed", ex);
        }
    }
}
