namespace FinViet.Infrastructure.ExternalServices.VNPay;

public sealed class VNPayOptions
{
    public const string SectionName = "VNPay";

    /// <summary>Merchant terminal code (vnp_TmnCode) issued by VNPay. Empty disables the integration.</summary>
    public string TmnCode { get; set; } = string.Empty;

    /// <summary>HMAC-SHA512 secret key used to sign/verify vnp_SecureHash. User-secrets/env only, never committed.</summary>
    public string HashSecret { get; set; } = string.Empty;

    /// <summary>Payment page the customer's browser is redirected to. Sandbox vs. production differ by host only.</summary>
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";

    /// <summary>
    /// Server-to-server recurring/token-charge endpoint. VNPay's exact recurring-billing API
    /// shape (path, request/response field names) must be confirmed against VNPay's merchant
    /// recurring-billing documentation once real sandbox credentials are available — this is a
    /// known, called-out gap (see context/current-feature.md). Left empty by default; a charge
    /// attempt with this unset throws IntegrationUnavailableException, same as an empty TmnCode.
    /// </summary>
    public string RecurringChargeUrl { get; set; } = string.Empty;

    public string Version { get; set; } = "2.1.0";

    public string CurrCode { get; set; } = "VND";

    public string Locale { get; set; } = "vn";

    /// <summary>Where VNPay redirects the customer's browser after payment. Informational only — the IPN is authoritative.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
