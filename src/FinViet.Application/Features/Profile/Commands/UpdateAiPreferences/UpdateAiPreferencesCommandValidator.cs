using FluentValidation;

namespace FinViet.Application.Features.Profile.Commands.UpdateAiPreferences;

public class UpdateAiPreferencesCommandValidator : AbstractValidator<UpdateAiPreferencesCommand>
{
    private static readonly string[] Modes = ["off", "suggest_only", "high_confidence_auto"];

    public UpdateAiPreferencesCommandValidator()
    {
        RuleFor(x => x.CategorizationMode)
            .Must(mode => mode is not null && Modes.Contains(mode.Trim().ToLowerInvariant()))
            .WithMessage("categorizationMode must be off, suggest_only, or high_confidence_auto.")
            .When(x => x.CategorizationMode is not null);

        RuleFor(x => x.AutoCategorizationThreshold)
            .GreaterThan(0m)
            .LessThanOrEqualTo(1m)
            .When(x => x.AutoCategorizationThreshold.HasValue);
    }
}
