using FinViet.Infrastructure.ExternalServices;
using Newtonsoft.Json.Linq;

namespace FinViet.Application.UnitTests.Authentication;

/// <summary>
/// Guards the <c>sign_in_provider</c> check in <see cref="FirebaseAuthService"/>.
/// </summary>
/// <remarks>
/// The JObject case is the regression: FirebaseAdmin builds nested claims with
/// Newtonsoft, and reading one with System.Text.Json silently rendered every leaf as
/// an empty array, so every genuine Google sign-in was rejected as an invalid token.
/// </remarks>
public class FirebaseSignInProviderTests
{
    private const string GoogleClaimJson =
        """{"identities":{"google.com":["115000000000000000000"],"email":["a@b.com"]},"sign_in_provider":"google.com"}""";

    [Fact]
    public void ReadSignInProvider_NewtonsoftJObjectClaim_ReadsGoogleProvider()
    {
        var claim = JObject.Parse(GoogleClaimJson);

        Assert.Equal("google.com", FirebaseAuthService.ReadSignInProvider(claim));
    }

    [Fact]
    public void ReadSignInProvider_DictionaryClaim_ReadsGoogleProvider()
    {
        var claim = new Dictionary<string, object> { ["sign_in_provider"] = "google.com" };

        Assert.Equal("google.com", FirebaseAuthService.ReadSignInProvider(claim));
    }

    [Fact]
    public void ReadSignInProvider_PasswordSignIn_IsNotReportedAsGoogle()
    {
        var claim = JObject.Parse("""{"sign_in_provider":"password"}""");

        Assert.Equal("password", FirebaseAuthService.ReadSignInProvider(claim));
    }

    [Fact]
    public void ReadSignInProvider_ClaimWithoutProvider_ReturnsNull()
    {
        var claim = JObject.Parse("""{"identities":{}}""");

        Assert.Null(FirebaseAuthService.ReadSignInProvider(claim));
    }

    [Fact]
    public void ReadSignInProvider_NonStringProvider_ReturnsNull()
    {
        var claim = JObject.Parse("""{"sign_in_provider":["google.com"]}""");

        Assert.Null(FirebaseAuthService.ReadSignInProvider(claim));
    }
}
