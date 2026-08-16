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

    // 50-30-20 budget bucket allocation — source of truth consumed by
    // IBudgetService.GetBudgetBucketsAsync for bucket scoring. Defaults match the
    // Customer entity's column defaults so admin profiles (which have no allocation)
    // still serialize a sane value.
    public decimal NeedsPct { get; set; } = 50;
    public decimal WantsPct { get; set; } = 30;
    public decimal SavingsPct { get; set; } = 20;

    // Per-customer preferences (customer_settings) — default when the customer has no
    // settings row yet (nothing writes one until UpdateProfileSettingsCommand is first called).
    public AppTheme Theme { get; set; } = AppTheme.System;
    public int[] NotifBudgetThresholds { get; set; } = { 80, 100 };
}
