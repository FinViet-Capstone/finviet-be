using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Commands.ConfirmPayOSWebhook;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.PayOS;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.Features.Subscriptions.Commands.ConfirmPayOSWebhook;

internal sealed class ConfirmPayOSWebhookCommandHandler(
    IPaymentGateway gateway,
    IOptions<PayOSOptions> options,
    ILogger<ConfirmPayOSWebhookCommandHandler> logger)
    : IRequestHandler<ConfirmPayOSWebhookCommand, ConfirmPayOSWebhookResultDto>
{
    public async Task<ConfirmPayOSWebhookResultDto> Handle(
        ConfirmPayOSWebhookCommand request, CancellationToken cancellationToken)
    {
        var webhookUrl = string.IsNullOrWhiteSpace(request.WebhookUrl)
            ? options.Value.WebhookUrl
            : request.WebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new BadRequestException(
                "No webhook URL supplied and PayOS:WebhookUrl is not configured.");
        }

        var result = await gateway.ConfirmWebhookAsync(webhookUrl, cancellationToken);

        logger.LogInformation(
            "Confirmed PayOS webhook URL {WebhookUrl} for account {AccountNumber}",
            result.WebhookUrl, result.AccountNumber);

        return new ConfirmPayOSWebhookResultDto(result.WebhookUrl, result.AccountName, result.AccountNumber);
    }
}
