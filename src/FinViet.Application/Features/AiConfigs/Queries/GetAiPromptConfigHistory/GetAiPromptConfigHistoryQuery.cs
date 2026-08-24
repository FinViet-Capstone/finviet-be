using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.AiConfigs.Queries.GetAiPromptConfigHistory;

public record GetAiPromptConfigHistoryQuery(string FeatureKey)
    : IRequest<IReadOnlyList<AiPromptConfigHistoryDto>>;
