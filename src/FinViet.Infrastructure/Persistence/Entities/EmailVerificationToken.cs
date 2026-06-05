namespace FinViet.Infrastructure.Persistence.Entities;

public class EmailVerificationToken
{
    public Guid TokenId { get; set; }
    public Guid CustomerId { get; set; }
    public string Token { get; set; } = null!;
    
    /// <summary>VERIFY_EMAIL or RESET_PASSWORD</summary>
    public string TokenType { get; set; } = null!;
    
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
