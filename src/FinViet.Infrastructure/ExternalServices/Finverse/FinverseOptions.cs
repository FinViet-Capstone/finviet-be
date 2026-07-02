namespace FinViet.Infrastructure.ExternalServices.Finverse;

public sealed class FinverseOptions
{
    public const string SectionName = "Finverse";

    public string BaseUrl { get; set; } = "https://api.prod.finverse.net/";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public int LinkStateLifetimeMinutes { get; set; } = 10;

    public int TransactionPageSize { get; set; } = 500;

    public int MaxTransactionPages { get; set; } = 20;
}
