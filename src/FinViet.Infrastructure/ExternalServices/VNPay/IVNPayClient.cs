namespace FinViet.Infrastructure.ExternalServices.VNPay;

internal interface IVNPayClient
{
    /// <summary>Builds the full, signed redirect URL for the customer's browser (vnp_Command=pay).</summary>
    string BuildPaymentUrl(VNPayPaymentRequest request);

    /// <summary>
    /// Server-to-server recurring charge using a previously-registered card token. Request/
    /// response shape is provisional pending real VNPay merchant recurring-billing docs — see
    /// VNPayOptions.RecurringChargeUrl.
    /// </summary>
    Task<VNPayChargeResult> ChargeByTokenAsync(
        string cardToken,
        decimal amountVnd,
        string txnRef,
        string orderInfo,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies vnp_SecureHash on an inbound param set (IPN or return-URL query params).</summary>
    bool VerifySecureHash(IReadOnlyDictionary<string, string> vnpParams);
}

internal sealed record VNPayPaymentRequest(
    decimal AmountVnd,
    string TxnRef,
    string OrderInfo,
    string IpAddress,
    string? ReturnUrlOverride = null);

internal sealed record VNPayChargeResult(
    bool Success,
    string? ResponseCode,
    string? TransactionNo,
    string? TransactionStatus,
    string? BankCode,
    string? CardType,
    string? PayDate,
    string? RawResponse);
