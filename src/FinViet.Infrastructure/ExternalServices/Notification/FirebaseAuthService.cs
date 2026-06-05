using FinViet.Application.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.ExternalServices;

public class FirebaseAuthService : IFirebaseAuthService
{
    private readonly ILogger<FirebaseAuthService> _logger;
    private readonly bool _enabled;

    public FirebaseAuthService(IConfiguration config, ILogger<FirebaseAuthService> logger)
    {
        _logger = logger;

        if (FirebaseApp.DefaultInstance is not null)
        {
            _enabled = true;
            return;
        }

        var credentialPath = config["Firebase:ServiceAccountJsonPath"];
        var projectId      = config["Firebase:ProjectId"];

        try
        {
            if (!string.IsNullOrEmpty(credentialPath) && File.Exists(credentialPath))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credentialPath),
                    ProjectId  = projectId ?? "finviet"
                });
                _enabled = true;
            }
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.GetApplicationDefault(),
                    ProjectId  = projectId ?? "finviet"
                });
                _enabled = true;
            }
            else
            {
                _logger.LogWarning(
                    "Firebase is not configured (no service account JSON or GOOGLE_APPLICATION_CREDENTIALS). " +
                    "Google login will return 503 until configured.");
                _enabled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase. Google login will be disabled.");
            _enabled = false;
        }
    }

    public async Task<FirebaseUserInfo?> VerifyIdTokenAsync(string idToken)
    {
        if (!_enabled)
        {
            _logger.LogWarning("Firebase token verification skipped — service is not configured.");
            return null;
        }

        try
        {
            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);

            decoded.Claims.TryGetValue("email",          out var email);
            decoded.Claims.TryGetValue("name",           out var name);
            decoded.Claims.TryGetValue("picture",        out var picture);
            decoded.Claims.TryGetValue("email_verified", out var emailVerified);

            return new FirebaseUserInfo(
                Uid:           decoded.Uid,
                Email:         email?.ToString(),
                DisplayName:   name?.ToString(),
                PhotoUrl:      picture?.ToString(),
                EmailVerified: emailVerified is bool b && b);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase ID token verification failed.");
            return null;
        }
    }
}