using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FinViet.Infrastructure.ExternalServices.VNPay;

/// <summary>
/// Implements VNPay's documented secure-hash algorithm exactly: collect all non-empty vnp_*
/// params (excluding vnp_SecureHash/vnp_SecureHashType), sort by key ordinal, join as
/// "urlencode(key)=urlencode(value)&..." (trailing "&" trimmed), HMAC-SHA512 that string with
/// the merchant hash secret, lowercase-hex encode. The same encoded string is both what's sent
/// on the wire (as the payment URL's query string) and what gets signed — this mirrors VNPay's
/// own reference implementation, not a simplified approximation.
/// </summary>
public static class VNPayHashHelper
{
    private const string SecureHashKey = "vnp_SecureHash";
    private const string SecureHashTypeKey = "vnp_SecureHashType";

    /// <summary>
    /// Builds the signed query string for outbound requests (payment URL, recurring charge
    /// requests): "key1=val1&key2=val2&...&vnp_SecureHash=&lt;hash&gt;", ready to append to a base URL.
    /// </summary>
    public static string BuildSignedQueryString(IDictionary<string, string> vnpParams, string hashSecret)
    {
        var (encodedQuery, _) = BuildEncodedQuery(vnpParams);
        var hash = ComputeHmacSha512(encodedQuery, hashSecret);
        return string.IsNullOrEmpty(encodedQuery)
            ? $"{SecureHashKey}={hash}"
            : $"{encodedQuery}&{SecureHashKey}={hash}";
    }

    /// <summary>Computes the secure hash over the given params (excluding the hash fields themselves).</summary>
    public static string Sign(IDictionary<string, string> vnpParams, string hashSecret)
    {
        var (encodedQuery, _) = BuildEncodedQuery(vnpParams);
        return ComputeHmacSha512(encodedQuery, hashSecret);
    }

    /// <summary>
    /// Recomputes the hash over an inbound param set (e.g. an IPN query string or return-URL
    /// params) and compares it to the received vnp_SecureHash using a constant-time comparison —
    /// same convention as SepayWalletService.HandleWebhookAsync's FixedTimeEquals usage.
    /// </summary>
    public static bool Verify(IReadOnlyDictionary<string, string> vnpParams, string hashSecret)
    {
        if (!vnpParams.TryGetValue(SecureHashKey, out var receivedHash) || string.IsNullOrWhiteSpace(receivedHash))
            return false;

        var toSign = vnpParams
            .Where(kv => !string.Equals(kv.Key, SecureHashKey, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(kv.Key, SecureHashTypeKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var computedHash = Sign(toSign, hashSecret);

        var computedBytes = Encoding.UTF8.GetBytes(computedHash.ToLowerInvariant());
        var receivedBytes = Encoding.UTF8.GetBytes(receivedHash.ToLowerInvariant());
        return computedBytes.Length == receivedBytes.Length
               && CryptographicOperations.FixedTimeEquals(computedBytes, receivedBytes);
    }

    private static (string EncodedQuery, SortedDictionary<string, string> Sorted) BuildEncodedQuery(
        IDictionary<string, string> vnpParams)
    {
        // VNPay's own reference implementation uses a SortedList<string,string> (ordinal) and
        // skips empty values entirely — both must be replicated exactly for the hash to match.
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in vnpParams)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (string.Equals(key, SecureHashKey, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, SecureHashTypeKey, StringComparison.OrdinalIgnoreCase)) continue;
            sorted[key] = value;
        }

        var builder = new StringBuilder();
        foreach (var (key, value) in sorted)
        {
            if (builder.Length > 0) builder.Append('&');
            builder.Append(WebUtility.UrlEncode(key)).Append('=').Append(WebUtility.UrlEncode(value));
        }

        return (builder.ToString(), sorted);
    }

    private static string ComputeHmacSha512(string data, string hashSecret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
