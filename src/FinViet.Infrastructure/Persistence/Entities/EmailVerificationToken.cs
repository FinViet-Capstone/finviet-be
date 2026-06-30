using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

public class EmailVerificationToken
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid TokenId { get; set; }
    public Guid CustomerId { get; set; }
    public string Token { get; set; } = null!;

    /// <summary>Postgres enum <c>email_token_type</c> (verify_email / reset_password).</summary>
    public EmailTokenType TokenType { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
