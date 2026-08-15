using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.VNPay;

internal sealed class VNPayClient : IVNPayClient
{
    private readonly HttpClient _http;
    private readonly VNPayOptions _options;
    private readonly ILogger<VNPayClient> _logger;

    public VNPayClient(HttpClient http, IOptions<VNPayOptions> options, ILogger<VNPayClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string BuildPaymentUrl(VNPayPaymentRequest request)
    {
        EnsureConfigured();

        var createDate = DateTime.UtcNow.AddHours(7).ToString("yyyyMMddHHmmss"); // Asia/Ho_Chi_Minh
        var vnpParams = new Dictionary<string, string>
        {
            ["vnp_Version"] = _options.Version,
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_Amount"] = ToWireAmount(request.AmountVnd).ToString(),
            ["vnp_CurrCode"] = _options.CurrCode,
            ["vnp_TxnRef"] = request.TxnRef,
            ["vnp_OrderInfo"] = request.OrderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"] = _options.Locale,
            ["vnp_ReturnUrl"] = request.ReturnUrlOverride ?? _options.ReturnUrl,
            ["vnp_IpAddr"] = request.IpAddress,
            ["vnp_CreateDate"] = createDate,
        };

        var query = VNPayHashHelper.BuildSignedQueryString(vnpParams, _options.HashSecret);
        return $"{_options.PaymentUrl}?{query}";
    }

    public async Task<VNPayChargeResult> ChargeByTokenAsync(
        string cardToken,
        decimal amountVnd,
        string txnRef,
        string orderInfo,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_options.RecurringChargeUrl))
        {
            throw new IntegrationUnavailableException(
                "VNPay recurring-charge endpoint is not configured.", "vnpay_recurring_not_configured");
        }

        // Request/response shape is provisional pending real VNPay merchant recurring-billing
        // docs (see VNPayOptions.RecurringChargeUrl) — the signed-param convention below mirrors
        // BuildPaymentUrl's since VNPay's server-to-server APIs use the same vnp_* + secure-hash
        // shape for every command, but the exact field set for a token charge is not yet verified
        // against a live sandbox.
        var createDate = DateTime.UtcNow.AddHours(7).ToString("yyyyMMddHHmmss");
        var vnpParams = new Dictionary<string, string>
        {
            ["vnp_Version"] = _options.Version,
            ["vnp_Command"] = "recurring_charge",
            ["vnp_TmnCode"] = _options.TmnCode,
            ["vnp_Amount"] = ToWireAmount(amountVnd).ToString(),
            ["vnp_CurrCode"] = _options.CurrCode,
            ["vnp_TxnRef"] = txnRef,
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_CardToken"] = cardToken,
            ["vnp_CreateDate"] = createDate,
        };
        vnpParams["vnp_SecureHash"] = VNPayHashHelper.Sign(vnpParams, _options.HashSecret);

        try
        {
            using var response = await _http.PostAsJsonAsync(_options.RecurringChargeUrl, vnpParams, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "VNPay recurring charge for {TxnRef} failed with HTTP {StatusCode}: {Body}",
                    txnRef, (int)response.StatusCode, raw);
                return new VNPayChargeResult(false, null, null, null, null, null, null, raw);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string? Field(string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

            var responseCode = Field("vnp_ResponseCode");
            var transactionStatus = Field("vnp_TransactionStatus");
            var success = responseCode == "00" && (transactionStatus is null or "00");

            return new VNPayChargeResult(
                success,
                responseCode,
                Field("vnp_TransactionNo"),
                transactionStatus,
                Field("vnp_BankCode"),
                Field("vnp_CardType"),
                Field("vnp_PayDate"),
                raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "VNPay recurring charge for {TxnRef} threw.", txnRef);
            throw new ExternalServiceException("VNPay recurring charge request failed.", "vnpay_request_failed", ex);
        }
    }

    public bool VerifySecureHash(IReadOnlyDictionary<string, string> vnpParams)
    {
        EnsureConfigured();
        return VNPayHashHelper.Verify(vnpParams, _options.HashSecret);
    }

    /// <summary>VNPay transmits VND with no minor unit as amount * 100. This is the only place the multiply happens.</summary>
    private static long ToWireAmount(decimal amountVnd) => (long)(amountVnd * 100m);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.TmnCode) || string.IsNullOrWhiteSpace(_options.HashSecret))
        {
            throw new IntegrationUnavailableException(
                "VNPay is not configured on this server.", "vnpay_not_configured");
        }
    }
}
