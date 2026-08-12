using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.Profile.Queries.GetAiPreferences;

public record GetAiPreferencesQuery(Guid CustomerId) : IRequest<AiPreferenceDto>;
