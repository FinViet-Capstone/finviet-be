using FinViet.Application.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Subscriptions.Commands.ProcessPayOSWebhook;

internal class ProcessPayOSWebhookCommandHandler : IRequestHandler<ProcessPayOSWebhookCommand, bool>
{
    private const string Pending = "pending";

    private readonly FinVietDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly ISubscriptionPaymentResultService _resultService;
    private readonly ILogger<ProcessPayOSWebhookCommandHandler> _logger;

    public ProcessPayOSWebhookCommandHandler(
        FinVietDbContext db,
        IPaymentGateway gateway,
        ISubscriptionPaymentResultService resultService,
        ILogger<ProcessPayOSWebhookCommandHandler> logger)
    {
        _db = db;
        _gateway = gateway;
        _resultService = resultService;
        _logger = logger;
    }

    public async Task<bool> Handle(ProcessPayOSWebhookCommand request, CancellationToken cancellationToken)
    {
        var verified = await _gateway.VerifyWebhookAsync(request.RawBody, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var payment = await PaymentLocking.LockByOrderCodeAsync(_db, verified.OrderCode, cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning("PayOS webhook for unknown orderCode {OrderCode}", verified.OrderCode);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (payment.Status != Pending)
        {
            _logger.LogInformation(
                "PayOS webhook for already-resolved payment {PaymentId} (status={Status}), no-op",
                payment.PaymentId, payment.Status);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await _resultService.ApplyResultAsync(
            payment,
            verified.Success,
            verified.Amount,
            verified.TransactionId,
            request.RawBody,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "PayOS webhook processed: orderCode={OrderCode}, paymentId={PaymentId}, success={Success}",
            verified.OrderCode, payment.PaymentId, verified.Success);

        return true;
    }
}
