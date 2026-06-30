namespace FinViet.Infrastructure.ExternalServices.Finverse;

/// <summary>Binds the "Finverse" section of appsettings.json.</summary>
public class FinverseOptions
{
    public const string SectionName = "Finverse";

    /// <summary>Finverse API base, e.g. sandbox: https://api.sandbox.finverse.net</summary>
    public string BaseUrl { get; set; } = "https://api.sandbox.finverse.net";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with Finverse. The mobile WebView intercepts navigation to this URL
    /// to capture the auth <c>code</c>; the page itself is never loaded.
    /// </summary>
    public string RedirectUri { get; set; } = "https://finviet.app/finverse/callback";

    /// <summary>"test" (test bank only), "real test" (both), or "" (real only).</summary>
    public string LinkMode { get; set; } = "test";

    public int TimeoutSeconds { get; set; } = 30;
}
