using Microsoft.AspNetCore.DataProtection;

namespace FinViet.Infrastructure.ExternalServices.SePay;

internal interface ISepayTokenProtector
{
    string Protect(string token);

    string Unprotect(string protectedToken);
}

internal sealed class SepayTokenProtector : ISepayTokenProtector
{
    private readonly IDataProtector _protector;

    public SepayTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("FinViet.SePay.OAuthTokens.v1");
    }

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}
