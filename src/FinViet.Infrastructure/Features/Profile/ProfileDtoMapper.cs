using FinViet.Application.DTOs;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Entities;

namespace FinViet.Infrastructure.Features.Profile;

/// <summary>
/// Single mapping point from <see cref="Customer"/> to <see cref="ProfileDto"/>, shared by
/// every handler that returns a profile (login, refresh-token, get/update profile). Keeping
/// this in one place avoids a field silently going missing from one of the response sites —
/// exactly what happened to NeedsPct/WantsPct/SavingsPct before this mapper existed.
///
/// Reads <see cref="Customer.Setting"/> for Theme/NotifBudgetThresholds — callers must
/// `.Include(x => x.Setting)` (or `.ThenInclude` via Customer) when fetching, or those two
/// fields will just fall back to their DTO defaults instead of the customer's saved values.
/// </summary>
internal static class ProfileDtoMapper
{
    public static ProfileDto ToProfileDto(this Customer c) => new()
    {
        CustomerId = c.CustomerId,
        FullName = c.FullName,
        Email = c.Email,
        AvatarUrl = c.AvatarUrl,
        Gender = c.Gender,
        DateOfBirth = c.DateOfBirth,
        MonthlyIncomeExpected = c.MonthlyIncomeExpected,
        IsEmailVerified = c.IsEmailVerified,
        IsActive = c.IsActive,
        OnboardingDone = c.OnboardingDone,
        CreatedAt = c.CreatedAt,
        NeedsPct = c.NeedsPct,
        WantsPct = c.WantsPct,
        SavingsPct = c.SavingsPct,
        Theme = c.Setting?.Theme ?? AppTheme.System,
        NotifBudgetThresholds = c.Setting?.NotifBudgetThresholds is { Length: 2 } t ? t : new[] { 80, 100 }
    };
}
