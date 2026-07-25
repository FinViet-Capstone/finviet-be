using Microsoft.AspNetCore.DataProtection;

namespace FinViet.Infrastructure.ExternalServices.SePay;

/// <summary>
/// Mints and verifies the OAuth2 <c>state</c> parameter. The customer id is signed and given an
/// expiry by Data Protection, so a redirect cannot be replayed later or swapped onto another
/// customer's session — the server never has to keep pending-link rows around.
/// </summary>
internal interface ISepayLinkStateProtector
{
    string Protect(Guid customerId, TimeSpan lifetime);

    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The state was tampered with, was issued by another application, or has expired.
    /// </exception>
    Guid UnprotectCustomerId(string state);
}

internal sealed class SepayLinkStateProtector : ISepayLinkStateProtector
{
    private readonly ITimeLimitedDataProtector _protector;

    public SepayLinkStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider
            .CreateProtector("FinViet.SePay.LinkState.v1")
            .ToTimeLimitedDataProtector();
    }

    // Data Protection emits base64url, so the payload is safe to carry in a query string as-is.
    public string Protect(Guid customerId, TimeSpan lifetime)
        => _protector.Protect(customerId.ToString("N"), lifetime);

    public Guid UnprotectCustomerId(string state)
        => Guid.ParseExact(_protector.Unprotect(state), "N");
}
