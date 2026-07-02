using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace FinViet.Infrastructure.ExternalServices.Finverse;

internal interface IFinverseTokenProtector
{
    string Protect(string token);

    string Unprotect(string protectedToken);
}

internal sealed class FinverseTokenProtector : IFinverseTokenProtector
{
    private readonly IDataProtector _protector;

    public FinverseTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("FinViet.Finverse.LoginIdentityTokens.v1");
    }

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}

internal interface IFinverseLinkStateProtector
{
    string Protect(Guid customerId, TimeSpan lifetime);

    Guid UnprotectCustomerId(string state);
}

internal sealed class FinverseLinkStateProtector : IFinverseLinkStateProtector
{
    private readonly ConcurrentDictionary<string, LinkState> _states = new(StringComparer.Ordinal);

    public string Protect(Guid customerId, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        RemoveExpiredStates();

        string state;
        do
        {
            // Finverse rejects state values around 150 characters. A 256-bit opaque
            // nonce is 43 Base64Url characters while remaining infeasible to guess.
            state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        while (!_states.TryAdd(state, new LinkState(customerId, DateTimeOffset.UtcNow.Add(lifetime))));

        return state;
    }

    public Guid UnprotectCustomerId(string state)
    {
        if (string.IsNullOrWhiteSpace(state)
            || !_states.TryRemove(state, out var linkState)
            || linkState.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new CryptographicException("Invalid Finverse state payload.");
        }

        return linkState.CustomerId;
    }

    private void RemoveExpiredStates()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _states)
        {
            if (entry.Value.ExpiresAt <= now)
                _states.TryRemove(entry.Key, out _);
        }
    }

    private sealed record LinkState(Guid CustomerId, DateTimeOffset ExpiresAt);
}
