using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Audit record for every VNPay charge attempt (initial subscribe or a scheduled renewal).
/// Independent from personal-finance transactions and never changes a wallet balance.
/// </summary>
public partial class Payment
{
    public Guid PaymentId { get; set; }

    /// <summary>
    /// Null until the IPN handler confirms an "initial" charge and creates the
    /// CustomerSubscription row (see ProcessVNPayIpnCommandHandler) — a subscription can't exist
    /// before its first payment succeeds.
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid PlanId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>"initial" or "renewal".</summary>
    public string ChargeType { get; set; } = null!;

    /// <summary>Postgres enum <c>payment_status</c> (pending/succeeded/failed/canceled).</summary>
    public string Status { get; set; } = null!;

    public string VnpTxnRef { get; set; } = null!;

    public string? VnpTransactionNo { get; set; }

    public string? VnpResponseCode { get; set; }

    public string? VnpTransactionStatus { get; set; }

    public string? VnpBankCode { get; set; }

    public string? VnpCardType { get; set; }

    /// <summary>Raw vnp_PayDate (yyyyMMddHHmmss), stored as-is for audit purposes.</summary>
    public string? VnpPayDate { get; set; }

    public DateTime? PaidAt { get; set; }

    public string? RawIpnPayload { get; set; }

    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CustomerSubscription? Subscription { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual SubscriptionPlan Plan { get; set; } = null!;
}
