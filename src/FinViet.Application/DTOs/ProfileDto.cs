using FinViet.Domain.Enums;

namespace FinViet.Application.DTOs;

public class ProfileDto
{
    public Guid CustomerId { get; set; }
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public decimal? MonthlyIncomeExpected { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsActive { get; set; }
    public bool OnboardingDone { get; set; }
    public DateTime? CreatedAt { get; set; }
}
