using FinViet.Application.DTOs;
using MediatR;

namespace FinViet.Application.Features.Profile.Queries.GetProfile;

public record GetProfileQuery(Guid CustomerId) : IRequest<ProfileDto>;
