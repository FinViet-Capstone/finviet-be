namespace FinViet.Infrastructure.Services;

/// <summary>
/// Bank account numbers are stored in full (SePay matches webhooks on them) but never leave the
/// API that way — every response exposes only the last four digits.
/// </summary>
internal static class AccountNumberMask
{
    public static string? Apply(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            return null;

        var trimmed = accountNumber.Trim();
        return trimmed.Length <= 4 ? trimmed : $"****{trimmed[^4..]}";
    }
}
