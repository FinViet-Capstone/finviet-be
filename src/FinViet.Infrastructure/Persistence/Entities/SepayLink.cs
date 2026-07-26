namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// SePay OAuth metadata for one linked wallet. OAuth tokens are encrypted via ASP.NET Data
/// Protection before being stored in the <c>access_token</c> / <c>refresh_token</c> columns.
/// </summary>
public sealed class SepayLink
{
    public Guid WalletId { get; set; }

    /// <summary>
    /// "oauth"  = OAuth2 authorization-code flow (access + refresh token).
    /// "static" = personal SePay User API token (single long-lived token, no refresh).
    /// </summary>
    public string AuthMode { get; set; } = "oauth";

    /// <summary>SePay user id (from GET /api/v1/me). Null for static-token links.</summary>
    public string? SepayUserId { get; set; }

    /// <summary>SePay bank account id (from GET /api/v1/bank-accounts).</summary>
    public int SepayBankAccountId { get; set; }

    public string? AccountNumber { get; set; }

    public string? AccountHolderName { get; set; }

    public string? BankShortName { get; set; }

    /// <summary>OAuth access token — encrypted by Data Protection.</summary>
    public string? AccessTokenProtected { get; set; }

    /// <summary>OAuth refresh token — encrypted by Data Protection.</summary>
    public string? RefreshTokenProtected { get; set; }

    /// <summary>
    /// When <see cref="AccessTokenProtected"/> stops being valid, derived from the token
    /// response's <c>expires_in</c>. Null for static links (their token never expires) and for
    /// OAuth links created before this was tracked — both fall back to refreshing on demand.
    /// </summary>
    public DateTime? AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// Id of the webhook FinViet registered on SePay for this bank account, so it can be removed
    /// again on unlink. Null when nothing is registered, or when the webhook was created by hand
    /// in the SePay dashboard.
    /// </summary>
    public int? SepayWebhookId { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
}
