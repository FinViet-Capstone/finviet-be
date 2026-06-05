using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Commands.UpdateProfile;

public record UpdateProfileCommand(
    Guid CustomerId,
    string FullName,
    decimal? MonthlyIncomeExpected
) : IRequest<ProfileDto>;
