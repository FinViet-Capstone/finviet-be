namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// Finverse metadata for one linked wallet. Tokens are protected by ASP.NET Data Protection
/// before being stored in the <c>access_token</c> / <c>refresh_token</c> text columns.
/// </summary>
public sealed class FinverseLink
{
    public Guid WalletId { get; set; }

    public string LoginIdentityId { get; set; } = string.Empty;

    public string? AccessTokenProtected { get; set; }

    public string? RefreshTokenProtected { get; set; }

    public string? FinverseAccountId { get; set; }

    public string? InstitutionName { get; set; }

    public string? AccountMask { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
}
