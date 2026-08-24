using FinViet.Application.DTOs.Ai;
using MediatR;

namespace FinViet.Application.Features.AiConfigs.Commands.UpdateAiPromptConfig;

public record UpdateAiPromptConfigCommand(
    string FeatureKey,
    Guid AdminId,
    UpdateAiPromptConfigRequest Request
) : IRequest<AiPromptConfigDto>;
