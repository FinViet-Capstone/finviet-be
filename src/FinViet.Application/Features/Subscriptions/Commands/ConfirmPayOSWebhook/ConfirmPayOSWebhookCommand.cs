using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Commands.ConfirmPayOSWebhook;

public record ConfirmPayOSWebhookCommand(string? WebhookUrl) : IRequest<ConfirmPayOSWebhookResultDto>;
