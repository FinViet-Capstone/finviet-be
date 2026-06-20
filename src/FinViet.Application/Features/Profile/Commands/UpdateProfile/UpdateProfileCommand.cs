using FinViet.Application.DTOs;
using FinViet.Domain.Enums;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.UpdateProfile;

public record UpdateProfileCommand(
    Guid CustomerId,
    string FullName,
    decimal? MonthlyIncomeExpected,
    Gender? Gender = null,
    DateOnly? DateOfBirth = null
) : IRequest<ProfileDto>;
