namespace FinViet.Infrastructure.ExternalServices.SePay;

public sealed class SepayOptions
{
    public const string SectionName = "SePay";

    /// <summary>SePay API base URL (resource endpoints).</summary>
    public string BaseUrl { get; set; } = "https://my.sepay.vn";

    /// <summary>
    /// OAuth2 authorization endpoint the client opens in a WebView. Relative paths are resolved
    /// against <see cref="BaseUrl"/>.
    /// </summary>
    public string AuthorizeUrl { get; set; } = "/oauth/authorize";

    /// <summary>OAuth2 client_id from SePay developer console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client_secret from SePay developer console.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth2 redirect URI registered with SePay.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Space-separated scopes requested during authorization. The webhook scopes are what let
    /// FinViet register its own receiver instead of the user pasting the URL into the SePay
    /// dashboard; drop them if you would rather wire webhooks up by hand.
    /// </summary>
    public string Scopes { get; set; } =
        "profile bank-account:read transaction:read webhook:read webhook:write webhook:delete";

    /// <summary>How long a signed OAuth <c>state</c> stays valid (minutes, clamped to 1–30).</summary>
    public int LinkStateLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// Shared secret SePay sends as <c>Authorization: Apikey &lt;key&gt;</c> on webhook calls.
    /// Leave empty to disable the webhook endpoint entirely.
    /// </summary>
    public string WebhookApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Publicly reachable URL of this API's webhook receiver, e.g.
    /// <c>https://api.finviet.vn/api/wallets/sepay/webhook</c>. SePay refuses private hosts, so a
    /// localhost value only works behind a tunnel. Required to auto-register webhooks.
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Name shown for the auto-registered webhook in the SePay dashboard.</summary>
    public string WebhookName { get; set; } = "FinViet";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum transaction pages to fetch during sync (pagination safety).</summary>
    public int MaxTransactionPages { get; set; } = 20;

    /// <summary>Transactions per page when fetching from SePay.</summary>
    public int TransactionPageSize { get; set; } = 100;

    /// <summary>Rows requested per call from the static User API (SePay caps this at 5000).</summary>
    public int UserApiPageSize { get; set; } = 5000;

    /// <summary>
    /// How far before a stored access token actually expires it is treated as expired, so a sync
    /// never starts with a token that dies mid-flight.
    /// </summary>
    public int AccessTokenExpirySkewSeconds { get; set; } = 120;

    /// <summary>
    /// Days of overlap re-fetched on an incremental sync, so postings that landed late still get
    /// picked up. Deduplication on <c>external_id</c> absorbs the overlap.
    /// </summary>
    public int SyncOverlapDays { get; set; } = 3;

    /// <summary>Transient-failure retries (429 / 5xx / network) per SePay request.</summary>
    public int MaxRetries { get; set; } = 2;
}
