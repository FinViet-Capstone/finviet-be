using FluentValidation;

namespace FinViet.Application.Features.Profile.Commands.UpdateProfileSettings;

public class UpdateProfileSettingsCommandValidator : AbstractValidator<UpdateProfileSettingsCommand>
{
    public UpdateProfileSettingsCommandValidator()
    {
        RuleFor(x => x.NotifBudgetThresholds)
            .Must(t => t!.Length == 2).WithMessage("notifBudgetThresholds must have exactly 2 values: [warning, exceeded].")
            .When(x => x.NotifBudgetThresholds is not null);

        RuleFor(x => x.NotifBudgetThresholds)
            .Must(t => t!.All(v => v is > 0 and <= 100))
            .WithMessage("notifBudgetThresholds values must be between 1 and 100.")
            .When(x => x.NotifBudgetThresholds is { Length: 2 });

        RuleFor(x => x.NotifBudgetThresholds)
            .Must(t => t![0] < t[1])
            .WithMessage("The warning threshold must be lower than the exceeded threshold.")
            .When(x => x.NotifBudgetThresholds is { Length: 2 });
    }
}
