using MediatR;

namespace FinViet.Application.Features.Subscriptions.Commands.ProcessPayOSWebhook;

public record ProcessPayOSWebhookCommand(string RawBody) : IRequest<bool>;
