namespace FinViet.Infrastructure.ExternalServices.SePay;

public sealed class SepayOptions
{
    public const string SectionName = "SePay";

    /// <summary>SePay API base URL (resource endpoints).</summary>
    public string BaseUrl { get; set; } = "https://my.sepay.vn";

    /// <summary>OAuth2 client_id from SePay developer console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client_secret from SePay developer console.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth2 redirect URI registered with SePay.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Space-separated scopes requested during authorization.</summary>
    public string Scopes { get; set; } = "profile bank-account:read transaction:read";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum transaction pages to fetch during sync (pagination safety).</summary>
    public int MaxTransactionPages { get; set; } = 20;

    /// <summary>Transactions per page when fetching from SePay.</summary>
    public int TransactionPageSize { get; set; } = 100;
}
