namespace FinViet.Infrastructure.Persistence.Entities;

public class RefreshToken
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid TokenId { get; set; }
    public Guid CustomerId { get; set; }
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
