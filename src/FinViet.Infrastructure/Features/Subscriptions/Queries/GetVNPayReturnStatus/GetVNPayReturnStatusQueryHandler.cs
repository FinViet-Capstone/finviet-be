using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Queries.GetVNPayReturnStatus;
using FinViet.Infrastructure.ExternalServices.VNPay;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Subscriptions.Queries.GetVNPayReturnStatus;

/// <summary>
/// Informational only — the browser return leg is never authoritative (a customer can close the
/// tab before it fires, or VNPay's IPN can race it). This handler never writes Payment or
/// CustomerSubscription state; it only reports whatever the IPN has (or hasn't yet) resolved.
/// </summary>
internal class GetVNPayReturnStatusQueryHandler : IRequestHandler<GetVNPayReturnStatusQuery, VNPayReturnStatusDto>
{
    private readonly FinVietDbContext _db;
    private readonly IVNPayClient _vnpay;

    public GetVNPayReturnStatusQueryHandler(FinVietDbContext db, IVNPayClient vnpay)
    {
        _db = db;
        _vnpay = vnpay;
    }

    public async Task<VNPayReturnStatusDto> Handle(GetVNPayReturnStatusQuery request, CancellationToken cancellationToken)
    {
        // A mismatched hash on the return leg is not fatal — logged via HashValid=false rather
        // than thrown, since the IPN (not this handler) is what actually gates state changes.
        var hashValid = _vnpay.VerifySecureHash(request.VnpParams);

        if (!request.VnpParams.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return new VNPayReturnStatusDto { Status = "unknown", HashValid = hashValid };

        var payment = await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.VnpTxnRef == txnRef, cancellationToken);

        if (payment is null)
            return new VNPayReturnStatusDto { Status = "unknown", HashValid = hashValid };

        return new VNPayReturnStatusDto
        {
            Status = payment.Status,
            HashValid = hashValid,
            SubscriptionId = payment.SubscriptionId,
        };
    }
}
