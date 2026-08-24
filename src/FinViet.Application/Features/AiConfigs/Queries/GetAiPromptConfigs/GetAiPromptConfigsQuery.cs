using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigs;

public record GetAiPromptConfigsQuery : IRequest<IReadOnlyList<AiPromptConfigDto>>;
