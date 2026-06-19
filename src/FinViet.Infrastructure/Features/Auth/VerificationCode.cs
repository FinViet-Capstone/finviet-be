using System.Security.Cryptography;

namespace FinViet.Infrastructure.Features.Auth;

/// <summary>
/// Generates short, human-friendly email verification codes (uppercase letters
/// + digits). Ambiguous characters (0/O, 1/I/L) are excluded to cut typing
/// errors. Uniqueness among active tokens is enforced by the caller.
/// </summary>
public static class VerificationCode
{
    private const string Charset = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Charset[RandomNumberGenerator.GetInt32(Charset.Length)];
        return new string(chars);
    }
}
