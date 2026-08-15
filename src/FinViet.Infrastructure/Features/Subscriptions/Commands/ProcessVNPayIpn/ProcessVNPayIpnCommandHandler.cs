using System.Text.Json;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Commands.ProcessVNPayIpn;
using FinViet.Infrastructure.ExternalServices.VNPay;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.Features.Subscriptions.Commands.ProcessVNPayIpn;

/// <summary>
/// Authoritative — see ProcessVNPayIpnCommand's summary. Deliberately never throws: every path
/// returns a VNPayIpnReplyDto, since VNPay's retry/ack behavior is driven entirely by the RspCode
/// in the JSON body, not by HTTP status (SubscriptionsController.VNPayIpn always returns 200).
/// </summary>
internal class ProcessVNPayIpnCommandHandler : IRequestHandler<ProcessVNPayIpnCommand, VNPayIpnReplyDto>
{
    private const string Pending = "pending";

    private readonly FinVietDbContext _db;
    private readonly IVNPayClient _vnpay;
    private readonly ISubscriptionPaymentResultService _resultService;
    private readonly ILogger<ProcessVNPayIpnCommandHandler> _logger;

    public ProcessVNPayIpnCommandHandler(
        FinVietDbContext db,
        IVNPayClient vnpay,
        ISubscriptionPaymentResultService resultService,
        ILogger<ProcessVNPayIpnCommandHandler> logger)
    {
        _db = db;
        _vnpay = vnpay;
        _resultService = resultService;
        _logger = logger;
    }

    public async Task<VNPayIpnReplyDto> Handle(ProcessVNPayIpnCommand request, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessAsync(request.VnpParams, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessVNPayIpnCommandHandler threw unexpectedly.");
            return VNPayIpnReplyDto.Of("99", "Unknown error");
        }
    }

    private async Task<VNPayIpnReplyDto> ProcessAsync(
        IReadOnlyDictionary<string, string> vnpParams,
        CancellationToken cancellationToken)
    {
        if (!_vnpay.VerifySecureHash(vnpParams))
            return VNPayIpnReplyDto.Of("97", "Invalid signature");

        if (!vnpParams.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return VNPayIpnReplyDto.Of("01", "Order not found");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Row-lock the Payment so a concurrent duplicate IPN can't race past the terminal-state
        // check below — same FOR UPDATE-inside-explicit-transaction idiom as WalletService's
        // balance-mutating paths.
        var payment = await _db.Payments
            .FromSqlInterpolated($"""
                SELECT id, subscription_id, customer_id, plan_id, amount, charge_type, status,
                       vnp_txn_ref, vnp_transaction_no, vnp_response_code, vnp_transaction_status,
                       vnp_bank_code, vnp_card_type, vnp_pay_date, paid_at, raw_ipn_payload,
                       idempotency_key, created_at, updated_at
                FROM payments WHERE vnp_txn_ref = {txnRef} FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return VNPayIpnReplyDto.Of("01", "Order not found");
        }

        if (!vnpParams.TryGetValue("vnp_Amount", out var amountRaw) || !long.TryParse(amountRaw, out var wireAmount))
        {
            await transaction.CommitAsync(cancellationToken);
            return VNPayIpnReplyDto.Of("04", "Invalid amount");
        }

        // VNPay transmits VND with no minor unit as amount * 100 — this is the only place the
        // divide happens (mirrors VNPayClient's single multiply on the outbound side).
        var receivedAmount = wireAmount / 100m;
        if (receivedAmount != payment.Amount)
        {
            await transaction.CommitAsync(cancellationToken);
            return VNPayIpnReplyDto.Of("04", "Invalid amount");
        }

        if (payment.Status != Pending)
        {
            // Terminal state under lock — this is the idempotency backstop for VNPay's own IPN
            // retries. Safe no-op, no further writes.
            await transaction.CommitAsync(cancellationToken);
            return VNPayIpnReplyDto.Of("02", "Order already confirmed");
        }

        vnpParams.TryGetValue("vnp_ResponseCode", out var responseCode);
        vnpParams.TryGetValue("vnp_TransactionStatus", out var transactionStatus);
        vnpParams.TryGetValue("vnp_TransactionNo", out var transactionNo);
        vnpParams.TryGetValue("vnp_BankCode", out var bankCode);
        vnpParams.TryGetValue("vnp_CardType", out var cardType);
        vnpParams.TryGetValue("vnp_PayDate", out var payDate);

        var success = responseCode == "00" && (transactionStatus is null or "00");

        payment.RawIpnPayload = JsonSerializer.Serialize(vnpParams);
        await _resultService.ApplyResultAsync(
            payment, success, responseCode, transactionStatus, transactionNo, bankCode, cardType, payDate,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return VNPayIpnReplyDto.Of("00", "Confirm success");
    }
}
