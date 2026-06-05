namespace FinViet.Application.Interfaces;

/// <summary>Represents a verified Firebase user identity.</summary>
public record FirebaseUserInfo(
    string Uid,
    string? Email,
    string? DisplayName,
    string? PhotoUrl,
    bool EmailVerified);

public interface IFirebaseAuthService
{
    /// <summary>
    /// Verifies a Firebase ID token issued by Google Sign-In on the client.
    /// Returns null if the token is invalid.
    /// </summary>
    Task<FirebaseUserInfo?> VerifyIdTokenAsync(string idToken);
}
