using FinViet.Application.Common;
using FluentValidation;

namespace FinViet.Application.Features.AiConfigs.Commands.UpdateAiPromptConfig;

public class UpdateAiPromptConfigCommandValidator : AbstractValidator<UpdateAiPromptConfigCommand>
{
    public UpdateAiPromptConfigCommandValidator()
    {
        RuleFor(x => x.FeatureKey)
            .Must(AiPromptFeatures.IsKnown)
            .WithMessage($"Feature key must be one of: {string.Join(", ", AiPromptFeatures.All)}.");

        RuleFor(x => x.Request.PersonaInstruction)
            .NotEmpty().WithMessage("Persona instruction is required.")
            .MaximumLength(4000).WithMessage("Persona instruction must be at most 4000 characters.");

        RuleFor(x => x.Request.Temperature)
            .InclusiveBetween(0m, 2m).WithMessage("Temperature must be between 0 and 2.");

        RuleFor(x => x.Request.MaxOutputTokens)
            .InclusiveBetween(16, 8192).WithMessage("Max output tokens must be between 16 and 8192.");
    }
}
