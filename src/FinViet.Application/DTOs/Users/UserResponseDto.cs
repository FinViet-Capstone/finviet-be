namespace FinViet.Application.DTOs.Users;

public class UserResponseDto
{
    public Guid CustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime? CreatedAt { get; set; }
}
