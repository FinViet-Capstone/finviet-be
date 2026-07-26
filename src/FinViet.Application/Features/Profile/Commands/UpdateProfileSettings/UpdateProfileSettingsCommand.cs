using FinViet.Application.DTOs;
using FinViet.Domain.Enums;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.UpdateProfileSettings;

public record UpdateProfileSettingsCommand(
    Guid CustomerId,
    AppTheme? Theme = null,
    int[]? NotifBudgetThresholds = null
) : IRequest<ProfileDto>;
